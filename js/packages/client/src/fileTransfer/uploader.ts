import { Subject, type HubConnection } from '@microsoft/signalr';
import { toByteSource, toUint8, type G9ByteSource } from '../byteSource.js';
import { abortErrorFrom } from '../errors.js';
import { fileByteSource, type G9FileSystem } from '../fileSystem.js';
import { delay, monotonicNow, raceAbort, safeInvoke, throwIfAborted } from '../internal/async.js';
import { isBinaryProtocol, resolveConnection, type G9ConnectionTarget } from '../internal/target.js';
import { sha256Hex, sha256HexOfString } from '../sha256.js';
import { waitUntilConnected } from '../waitUntilConnected.js';
import {
  G9UploadStatus,
  normalizeBeginUploadResult,
  normalizeServerUploadProgress,
  normalizeUploadResult,
  transferBackoffMs,
  type G9UploadProgress,
  type G9UploadResult,
  type G9UploadRetryInfo,
} from './dtos.js';
import { createWireChunk } from './wireChunk.js';

/** Options of {@link G9FileUploader}. Twin of `G9DtUploadClientOptions`. */
export interface G9FileUploaderOptions {
  /** Bytes per chunk. Default 64 KiB (the server may cap it). */
  chunkSize?: number;
  /** Retries (with exponential backoff `min(30 s, 200 ms × 2^attempt)`) before the upload fails. Default 5. */
  maxRetries?: number;
  /** Hub method of the begin handshake. Default `'BeginUpload'`. */
  beginMethod?: string;
  /** Hub method of the chunk stream. Default `'UploadChunks'`. */
  uploadMethod?: string;
  /** Called before every retry. */
  onRetry?: (info: G9UploadRetryInfo) => void;
  /**
   * How long an attempt waits for the connection to be connected again (after a network drop, SignalR's reconnect)
   * before calling the begin method. Default 120000 ms, the default of .NET's `ReconnectGrace`.
   */
  reconnectGraceMs?: number;
  /** Chunks handed to SignalR but not yet serialized by it; bounds memory. Default 8. */
  maxChunksInFlight?: number;
}

/** What {@link G9FileUploader.upload} accepts: a `Blob`/`File` (browsers), a {@link G9ByteSource} (Lynx, Node), or bytes. */
export type G9UploadInput =
  | G9ByteSource
  | ArrayBuffer
  | ArrayBufferView
  | { readonly size: number; slice(start?: number, end?: number): { arrayBuffer(): Promise<ArrayBuffer> } };

/** Per-call options of {@link G9FileUploader.upload}. */
export interface G9UploadOptions {
  /** Name declared to the server. Default: the source's `name` (a File, a file on disk), else `'blob'`. */
  fileName?: string;
  /**
   * Upload id. Default: lower-hex SHA-256 of the source's `uploadKey` (a file on disk: the .NET key
   * `lower(fullPath)|length|lastWriteTicks`), else of `${name}|${size}|${lastModified}` (stable across app restarts).
   */
  uploadId?: string;
  /** Cancels the upload (rejects with an `AbortError`; the server keeps the partial for a later resume). */
  signal?: AbortSignal;
  /** Progress sink (hashing, then uploading). */
  onProgress?: (progress: G9UploadProgress) => void;
  /** Listen to the server's `UploadProgress` callback for acknowledged bytes. Default true. */
  serverAck?: boolean;
  /** The file's lower-hex SHA-256 when already known (skips the hashing pass). */
  sha256?: string;
}

/** The server callback that acknowledges durable bytes. */
export const UPLOAD_PROGRESS_CALLBACK = 'UploadProgress';

/**
 * Resumable file uploader over SignalR client-to-server streaming. Twin of the .NET `G9CFileUploader`.
 *
 * 1. Hashes the file (SHA-256, 4 MiB slices) unless `sha256` is given.
 * 2. Calls `BeginUpload(uploadId, fileName, totalBytes, chunkSize, sha256)`; the server answers with the offset it
 *    already has (resume) or `alreadyCompleted`.
 * 3. Streams the remaining bytes into `UploadChunks(uploadId, stream)`, at most `maxChunksInFlight` chunks ahead of
 *    what SignalR has actually sent (base64 strings under JSON, raw bytes under MessagePack).
 * 4. Retries failed or interrupted attempts with the .NET backoff; each retry resumes from the server's offset.
 */
export class G9FileUploader {
  /** Default chunk size (64 KiB), as in .NET. */
  static readonly DEFAULT_CHUNK_SIZE = 64 * 1024;

  private readonly _target: G9ConnectionTarget;
  private readonly _chunkSize: number;
  private readonly _maxRetries: number;
  private readonly _beginMethod: string;
  private readonly _uploadMethod: string;
  private readonly _onRetry: ((info: G9UploadRetryInfo) => void) | undefined;
  private readonly _reconnectGraceMs: number;
  private readonly _maxInFlight: number;

  constructor(target: G9ConnectionTarget, options: G9FileUploaderOptions = {}) {
    if (target == null) throw new TypeError('A G9Client or HubConnection is required.');
    this._target = target;
    this._chunkSize = options.chunkSize ?? G9FileUploader.DEFAULT_CHUNK_SIZE;
    this._maxRetries = options.maxRetries ?? 5;
    this._beginMethod = options.beginMethod ?? 'BeginUpload';
    this._uploadMethod = options.uploadMethod ?? 'UploadChunks';
    this._onRetry = options.onRetry;
    this._reconnectGraceMs = options.reconnectGraceMs ?? 120000;
    this._maxInFlight = options.maxChunksInFlight ?? 8;

    if (!Number.isInteger(this._chunkSize) || this._chunkSize <= 0) throw new RangeError('chunkSize must be a positive integer.');
    if (!Number.isInteger(this._maxRetries) || this._maxRetries < 0) throw new RangeError('maxRetries must be a non-negative integer.');
    if (!Number.isInteger(this._maxInFlight) || this._maxInFlight < 1) throw new RangeError('maxChunksInFlight must be at least 1.');
    if (!(this._reconnectGraceMs >= 0)) throw new RangeError('reconnectGraceMs must be non-negative.');
  }

  /** The default upload id of a file: lower-hex SHA-256 of `${name}|${size}|${lastModified}`. */
  static computeUploadId(fileName: string, size: number, lastModified: number): string {
    return sha256HexOfString(`${fileName}|${size}|${lastModified}`);
  }

  /**
   * Uploads the file at `filePath` through `fileSystem`, exactly like the .NET `UploadAsync(filePath)`: the declared
   * name is the file name, and the default upload id is the .NET one (SHA-256 of
   * `lower(fullPath)|length|lastWriteTicks`), so an interrupted upload resumes after an app restart.
   */
  async uploadFile(
    filePath: string,
    options: G9UploadOptions & { fileSystem: G9FileSystem },
  ): Promise<G9UploadResult> {
    if (!options?.fileSystem) throw new TypeError('A G9FileSystem is required to upload a file by path.');
    throwIfAborted(options.signal);
    return this.upload(await fileByteSource(options.fileSystem, filePath), options);
  }

  /**
   * Uploads `file`, resuming a partial the server already holds for the same upload id. Resolves with the server's
   * result (a `Failed` status is a result, not an error); rejects when the retries are spent (with the last error),
   * or with an `AbortError` when `signal` aborts.
   */
  async upload(file: G9UploadInput, options: G9UploadOptions = {}): Promise<G9UploadResult> {
    const connection = resolveConnection(this._target);
    const signal = options.signal;
    throwIfAborted(signal);
    if (!file) throw new TypeError('A Blob, File, G9ByteSource or bytes are required.');
    const source = toByteSource(file);

    const totalBytes = source.size;
    if (totalBytes === 0) throw new Error('Refusing to upload a zero-byte file.');

    const fileName = options.fileName || source.name || 'blob';
    const lastModified = typeof source.lastModified === 'number' ? source.lastModified : 0;
    const uploadId =
      options.uploadId ??
      (source.uploadKey !== undefined
        ? sha256HexOfString(source.uploadKey)
        : G9FileUploader.computeUploadId(fileName, totalBytes, lastModified));
    const emit = (progress: G9UploadProgress): void => safeInvoke(options.onProgress, progress);

    // 1. Hash (the server verifies the committed bytes against it).
    let declaredSha = options.sha256?.trim().toLowerCase();
    if (!declaredSha) {
      const hashStarted = monotonicNow();
      declaredSha = await sha256Hex(
        source,
        (done, total) => {
          const elapsedMs = monotonicNow() - hashStarted;
          emit({
            uploadId,
            bytesSent: done,
            bytesAcknowledged: 0,
            totalBytes: total,
            bytesPerSecond: elapsedMs > 0 ? (done * 1000) / elapsedMs : 0,
            elapsedMs,
            phase: 'hashing',
          });
        },
        signal,
      );
    }

    // Uploading progress state, shared with the server-ack listener.
    let acked = 0;
    let sent = 0;
    let attemptOffset = 0;
    let attemptStarted = monotonicNow();
    const emitUploading = (): void => {
      const elapsedMs = monotonicNow() - attemptStarted;
      emit({
        uploadId,
        bytesSent: sent,
        bytesAcknowledged: acked,
        totalBytes,
        bytesPerSecond: elapsedMs > 0 ? ((sent - attemptOffset) * 1000) / elapsedMs : 0,
        elapsedMs,
        phase: 'uploading',
      });
    };

    let unsubscribeAck = (): void => undefined;
    if (options.serverAck ?? true) {
      const onAck = (raw: unknown): void => {
        const progress = normalizeServerUploadProgress(raw);
        if (!progress || progress.uploadId !== uploadId) return;
        acked = progress.bytesReceived;
        emitUploading();
      };
      connection.on(UPLOAD_PROGRESS_CALLBACK, onAck);
      unsubscribeAck = () => connection.off(UPLOAD_PROGRESS_CALLBACK, onAck);
    }

    try {
      for (let attempt = 0; ; attempt++) {
        throwIfAborted(signal);

        // After a network drop the begin call would fail at once while SignalR is still reconnecting.
        await waitUntilConnected(connection, this._reconnectGraceMs, signal);

        const begin = normalizeBeginUploadResult(
          await raceAbort(
            connection.invoke<unknown>(this._beginMethod, uploadId, fileName, totalBytes, this._chunkSize, declaredSha),
            signal,
          ),
        );

        if (begin.alreadyCompleted) {
          return {
            status: G9UploadStatus.Completed,
            bytesWritten: totalBytes,
            sha256: declaredSha,
            finalPath: begin.finalPath,
            storedFileName: begin.storedFileName,
            errorCode: null,
            errorMessage: null,
          };
        }

        const startOffset = Math.min(Math.max(0, begin.bytesAlreadyReceived), totalBytes);
        const chunkSize = begin.chunkSize > 0 ? begin.chunkSize : this._chunkSize;
        attemptOffset = startOffset;
        sent = startOffset;
        attemptStarted = monotonicNow();
        emitUploading();

        let result: G9UploadResult;
        try {
          result = await this._streamRemaining(connection, uploadId, source, totalBytes, startOffset, chunkSize, signal, (bytes) => {
            sent = bytes;
            emitUploading();
          });
        } catch (error) {
          if (signal?.aborted) throw abortErrorFrom(signal);
          if (attempt >= this._maxRetries) throw error;
          const backoffMs = transferBackoffMs(attempt);
          safeInvoke(this._onRetry, {
            reason: 'attemptFailed',
            attempt,
            maxRetries: this._maxRetries,
            bytesAlreadyOnServer: startOffset,
            backoffMs,
            error,
          });
          await delay(backoffMs, signal);
          continue;
        }

        if (result.status === G9UploadStatus.Interrupted && attempt < this._maxRetries) {
          const backoffMs = transferBackoffMs(attempt);
          safeInvoke(this._onRetry, {
            reason: 'interrupted',
            attempt,
            maxRetries: this._maxRetries,
            bytesAlreadyOnServer: result.bytesWritten,
            backoffMs,
          });
          await delay(backoffMs, signal);
          continue;
        }

        return result;
      }
    } finally {
      unsubscribeAck();
    }
  }

  /**
   * Streams `[startOffset, totalBytes)` of `source` into the upload method. Chunks are read one at a time, and
   * production pauses while `maxChunksInFlight` chunks are waiting in SignalR's send
   * queue, so memory stays bounded whatever the file size.
   */
  private async _streamRemaining(
    connection: HubConnection,
    uploadId: string,
    source: G9ByteSource,
    totalBytes: number,
    startOffset: number,
    chunkSize: number,
    signal: AbortSignal | undefined,
    onSent: (bytes: number) => void,
  ): Promise<G9UploadResult> {
    const binary = isBinaryProtocol(connection);
    const subject = new Subject<unknown>();

    let inFlight = 0;
    let settled = false;
    let waiter: (() => void) | null = null;
    const wake = (): void => {
      const resume = waiter;
      waiter = null;
      resume?.();
    };
    const release = (): void => {
      inFlight--;
      wake();
    };

    const resultPromise = connection.invoke<unknown>(this._uploadMethod, uploadId, subject);
    resultPromise.then(
      () => {
        settled = true;
        wake();
      },
      () => {
        settled = true;
        wake();
      },
    );
    signal?.addEventListener('abort', wake);

    let sent = startOffset;
    try {
      while (sent < totalBytes && !settled && !signal?.aborted) {
        while (inFlight >= this._maxInFlight && !settled && !signal?.aborted) {
          await new Promise<void>((resolve) => {
            waiter = resolve;
          });
        }
        if (settled || signal?.aborted) break;

        const end = Math.min(totalBytes, sent + chunkSize);
        const bytes = toUint8(await source.read(sent, end - sent));
        if (settled || signal?.aborted) break;
        if (bytes.length === 0) break; // the file shrank under us; the server will report the mismatch.

        inFlight++;
        subject.next(createWireChunk(bytes, binary, release));
        sent += bytes.length;
        onSent(sent);
      }

      if (signal?.aborted) {
        // The server sees the stream fail, keeps the partial and reports Interrupted; nobody waits for that.
        subject.error(new Error('The upload was cancelled by the client.'));
        resultPromise.catch(() => undefined);
        throw abortErrorFrom(signal);
      }
      if (!settled) subject.complete();
    } catch (error) {
      if (!signal?.aborted && !settled) subject.error(error instanceof Error ? error : new Error(String(error)));
      resultPromise.catch(() => undefined);
      throw error;
    } finally {
      signal?.removeEventListener('abort', wake);
    }

    return normalizeUploadResult(await raceAbort(resultPromise, signal));
  }
}
