import type { HubConnection, ISubscription } from '@microsoft/signalr';
import { abortErrorFrom, G9DownloadErrorCodes } from '../errors.js';
import { baseName, dirName, fileByteSource, type G9FileSystem } from '../fileSystem.js';
import { delay, describeError, monotonicNow, raceAbort, safeInvoke, throwIfAborted } from '../internal/async.js';
import { base64ToBytes } from '../internal/base64.js';
import { resolveConnection, type G9ConnectionTarget } from '../internal/target.js';
import { Sha256, sha256Hex } from '../sha256.js';
import { waitUntilConnected } from '../waitUntilConnected.js';
import {
  G9TransferError,
  G9UploadStatus,
  normalizeBeginDownloadResult,
  transferBackoffMs,
  type G9BeginDownloadResult,
  type G9DownloadResult,
  type G9UploadRetryInfo,
} from './dtos.js';

/** Options of {@link G9FileDownloader}. Twin of `G9DtDownloadClientOptions`. */
export interface G9FileDownloaderOptions {
  /** Bytes per chunk requested from the server. Default 64 KiB (the server may cap it). */
  chunkSize?: number;
  /** Retries (with exponential backoff) before the download fails. Default 5. */
  maxRetries?: number;
  /** Hub method of the begin handshake. Default `'BeginDownload'`. */
  beginMethod?: string;
  /** Hub method of the chunk stream (server-to-client streaming). Default `'DownloadChunks'`. */
  streamMethod?: string;
  /** Called before every retry. */
  onRetry?: (info: G9UploadRetryInfo) => void;
  /** Verify the assembled bytes against the SHA-256 the server announces (in-memory downloads). Default true. */
  verifySha256?: boolean;
  /** How long an attempt waits for the connection to be connected again before its begin call. Default 120000 ms. */
  reconnectGraceMs?: number;
  /**
   * {@link G9FileDownloader.downloadToFile}: when the target file already exists with the server's SHA-256, return
   * `Completed` without streaming. Twin of `ShortCircuitWhenComplete` (default true, as in .NET).
   */
  shortCircuitWhenComplete?: boolean;
}

/** Per-call options of {@link G9FileDownloader.download} and {@link G9FileDownloader.downloadBytes}. */
export interface G9DownloadOptions {
  /** Cancels the download (rejects with an `AbortError`). */
  signal?: AbortSignal;
  /** Progress sink: bytes received so far and the file size. */
  onProgress?: (received: number, total: number) => void;
  /** Chunk size for this call (overrides the constructor option). */
  chunkSize?: number;
  /** MIME type of the resulting Blob ({@link G9FileDownloader.download} only). */
  type?: string;
}

/** Per-call options of {@link G9FileDownloader.downloadToFile}. */
export interface G9DownloadToFileOptions {
  /** The file system that holds the target (Node: `nodeFileSystem`, Lynx: `lynxFileSystem()`). */
  fileSystem: G9FileSystem;
  /** Cancels the download: resolves with an `Interrupted` result and keeps the `.partial` file, as .NET does. */
  signal?: AbortSignal;
  /** Progress sink. Twin of `IProgress<G9DtDownloadClientProgress>`. */
  onProgress?: (progress: G9DownloadProgress) => void;
}

/** Twin of `G9DtDownloadClientProgress`. */
export interface G9DownloadProgress {
  fileName: string;
  bytesReceived: number;
  totalBytes: number;
  bytesPerSecond: number;
  elapsedMs: number;
}

/** Result of {@link G9FileDownloader.downloadToFile}. Twin of `G9DtDownloadResult` (with `LocalPath`). */
export interface G9FileDownloadResult extends G9DownloadResult {
  /** Where the file was committed (Completed), else null. */
  localPath: string | null;
}

/** Accumulated bytes flushed into Blob parts every 4 MiB, so large downloads can be paged out by the browser. */
const FLUSH_BYTES = 4 * 1024 * 1024;

/** Bytes buffered before a write to the `.partial` file (the .NET FileStream buffer is 1 MiB too). */
const FILE_BUFFER_BYTES = 1024 * 1024;

/** In-memory assembly: Blob parts (browsers) or byte chunks (every runtime), hashed as they arrive. */
class MemoryStore {
  private _parts: unknown[] = [];
  private _pending: Uint8Array[] = [];
  private _pendingBytes = 0;
  private _hash = new Sha256();
  size = 0;

  constructor(private readonly _asBlob: boolean) {}

  append(bytes: Uint8Array): void {
    this._hash.update(bytes);
    this._pending.push(bytes);
    this._pendingBytes += bytes.length;
    this.size += bytes.length;
    if (this._asBlob && this._pendingBytes >= FLUSH_BYTES) this._flush();
  }

  reset(): void {
    this._parts = [];
    this._pending = [];
    this._pendingBytes = 0;
    this._hash = new Sha256();
    this.size = 0;
  }

  sha256Hex(): string {
    return this._hash.hexDigest();
  }

  toBlob(type?: string): Blob {
    this._flush();
    return new Blob(this._parts as BlobPart[], type ? { type } : undefined);
  }

  toBytes(): Uint8Array {
    const out = new Uint8Array(this.size);
    let offset = 0;
    for (const chunk of this._pending) {
      out.set(chunk, offset);
      offset += chunk.length;
    }
    return out;
  }

  private _flush(): void {
    if (this._pending.length === 0) return;
    this._parts.push(new Blob(this._pending as BlobPart[]));
    this._pending = [];
    this._pendingBytes = 0;
  }
}

/** Decodes one stream item: base64 text (JSON protocol), bytes (MessagePack), or a number array. */
export function decodeChunk(item: unknown): Uint8Array {
  if (typeof item === 'string') return base64ToBytes(item);
  if (item instanceof Uint8Array) return item;
  if (item instanceof ArrayBuffer) return new Uint8Array(item);
  if (ArrayBuffer.isView(item)) return new Uint8Array(item.buffer, item.byteOffset, item.byteLength);
  if (Array.isArray(item)) return Uint8Array.from(item as number[]);
  if (item === null || item === undefined) return new Uint8Array(0);
  throw new TypeError(`Unexpected download chunk of type ${typeof item}.`);
}

/**
 * Resumable file downloader over SignalR server-to-client streaming. Twin of the .NET `G9CFileDownloader`:
 * `BeginDownload(fileName, resumeFrom, chunkSize)` then the `DownloadChunks(fileName, resumeFrom, chunkSize)` stream; a
 * failed or interrupted stream is retried with the .NET backoff and resumes from the bytes already received.
 *
 * - {@link downloadToFile} is the exact twin of `DownloadAsync(serverFileName, localTargetPath)`: a `.partial` file that
 *   survives cancellation and app restarts, SHA-256 verification, an atomic rename, a result object (Node, Lynx).
 * - {@link download} (a Blob, browsers) and {@link downloadBytes} (bytes, every runtime) assemble in memory and reject
 *   with a {@link G9TransferError} carrying the .NET error code (`G9_DOWNLOAD_NOT_FOUND`, `G9_DOWNLOAD_HASH_MISMATCH`,
 *   `G9_DOWNLOAD_FAILED`) and status.
 */
export class G9FileDownloader {
  /** Default chunk size (64 KiB), as in .NET. */
  static readonly DEFAULT_CHUNK_SIZE = 64 * 1024;

  private readonly _target: G9ConnectionTarget;
  private readonly _chunkSize: number;
  private readonly _maxRetries: number;
  private readonly _beginMethod: string;
  private readonly _streamMethod: string;
  private readonly _onRetry: ((info: G9UploadRetryInfo) => void) | undefined;
  private readonly _verify: boolean;
  private readonly _reconnectGraceMs: number;
  private readonly _shortCircuit: boolean;

  constructor(target: G9ConnectionTarget, options: G9FileDownloaderOptions = {}) {
    if (target == null) throw new TypeError('A G9Client or HubConnection is required.');
    this._target = target;
    this._chunkSize = options.chunkSize ?? G9FileDownloader.DEFAULT_CHUNK_SIZE;
    this._maxRetries = options.maxRetries ?? 5;
    this._beginMethod = options.beginMethod ?? 'BeginDownload';
    this._streamMethod = options.streamMethod ?? 'DownloadChunks';
    this._onRetry = options.onRetry;
    this._verify = options.verifySha256 ?? true;
    this._reconnectGraceMs = options.reconnectGraceMs ?? 120000;
    this._shortCircuit = options.shortCircuitWhenComplete ?? true;

    if (!Number.isInteger(this._chunkSize) || this._chunkSize <= 0) throw new RangeError('chunkSize must be a positive integer.');
    if (!Number.isInteger(this._maxRetries) || this._maxRetries < 0) throw new RangeError('maxRetries must be a non-negative integer.');
  }

  /**
   * Downloads `serverFileName` (e.g. an upload's `storedFileName`) and resolves with its content as a Blob. Needs a
   * `Blob` global (browsers, WebViews, Node); use {@link downloadBytes} or {@link downloadToFile} on Lynx.
   */
  async download(serverFileName: string, options: G9DownloadOptions = {}): Promise<Blob> {
    if (typeof Blob !== 'function') {
      throw new TypeError('This runtime has no Blob: use downloadBytes() or downloadToFile().');
    }
    const store = new MemoryStore(true);
    await this._downloadInMemory(serverFileName, options, store);
    return store.toBlob(options.type);
  }

  /** Downloads `serverFileName` into memory and resolves with its bytes. Works on every runtime, Lynx included. */
  async downloadBytes(serverFileName: string, options: G9DownloadOptions = {}): Promise<Uint8Array> {
    const store = new MemoryStore(false);
    await this._downloadInMemory(serverFileName, options, store);
    return store.toBytes();
  }

  private async _downloadInMemory(serverFileName: string, options: G9DownloadOptions, store: MemoryStore): Promise<void> {
    if (!serverFileName) throw new TypeError('A server file name is required.');
    const connection = resolveConnection(this._target);
    const signal = options.signal;
    const requestedChunk = options.chunkSize ?? this._chunkSize;
    if (!Number.isInteger(requestedChunk) || requestedChunk <= 0) throw new RangeError('chunkSize must be a positive integer.');

    const retry = async (attempt: number, error: unknown, interrupted: boolean): Promise<void> => {
      const backoffMs = transferBackoffMs(attempt);
      const info: G9UploadRetryInfo = {
        reason: interrupted ? 'interrupted' : 'attemptFailed',
        attempt,
        maxRetries: this._maxRetries,
        bytesAlreadyOnServer: store.size,
        backoffMs,
      };
      if (!interrupted) info.error = error;
      safeInvoke(this._onRetry, info);
      await delay(backoffMs, signal);
    };

    for (let attempt = 0; ; attempt++) {
      throwIfAborted(signal);

      let begin: G9BeginDownloadResult;
      try {
        await waitUntilConnected(connection, this._reconnectGraceMs, signal);
        begin = normalizeBeginDownloadResult(
          await raceAbort(connection.invoke<unknown>(this._beginMethod, serverFileName, store.size, requestedChunk), signal),
        );
      } catch (error) {
        if (signal?.aborted) throw abortErrorFrom(signal);
        if (attempt < this._maxRetries) {
          await retry(attempt, error, false);
          continue;
        }
        throw error;
      }

      if (begin.notFound) {
        throw new G9TransferError(
          G9DownloadErrorCodes.DownloadNotFound,
          `Server file '${serverFileName}' not found.`,
          G9UploadStatus.Failed,
          store.size,
        );
      }

      // Stale bytes beyond the server's file size: start over.
      if (store.size > begin.totalBytes) store.reset();

      const chunkSize = begin.chunkSize > 0 ? begin.chunkSize : requestedChunk;
      try {
        await this._streamInto(connection, serverFileName, store.size, chunkSize, signal, (bytes) => {
          store.append(bytes);
          safeInvoke(options.onProgress, store.size, begin.totalBytes);
        });
      } catch (error) {
        if (signal?.aborted) throw abortErrorFrom(signal);
        if (attempt < this._maxRetries) {
          await retry(attempt, error, false);
          continue;
        }
        throw new G9TransferError(
          G9DownloadErrorCodes.DownloadFailed,
          describeError(error),
          G9UploadStatus.Failed,
          store.size,
          error,
        );
      }

      // The stream ended early (server hung up, connection blip): resume from what we have.
      if (store.size < begin.totalBytes) {
        if (attempt < this._maxRetries) {
          await retry(attempt, undefined, true);
          continue;
        }
        throw new G9TransferError(
          null,
          `The download was interrupted after ${store.size} of ${begin.totalBytes} bytes.`,
          G9UploadStatus.Interrupted,
          store.size,
        );
      }

      if (this._verify && begin.sha256) {
        const actual = store.sha256Hex();
        if (actual !== begin.sha256.toLowerCase()) {
          throw new G9TransferError(
            G9DownloadErrorCodes.DownloadHashMismatch,
            `Server SHA-256 ${begin.sha256} != local ${actual}.`,
            G9UploadStatus.Failed,
            store.size,
          );
        }
      }
      return;
    }
  }

  /**
   * Downloads `serverFileName` to `localTargetPath`, resuming the `.partial` file a previous call left behind. Twin of
   * the .NET `DownloadAsync`: the bytes are appended to `<localTargetPath>.partial`, verified against the server's
   * SHA-256 and renamed into place. An existing unrelated target is not overwritten: the new file gets a
   * `.yyyyMMddHHmmssfff` stamp before its extension, and `localPath` says where it went. Cancellation resolves with
   * `Interrupted` and keeps the partial for the next call; transfer failures are results (`Failed` + `errorCode`). As in
   * .NET, a cancellation before the transfer starts and a begin call that fails after every retry throw.
   */
  async downloadToFile(
    serverFileName: string,
    localTargetPath: string,
    options: G9DownloadToFileOptions,
  ): Promise<G9FileDownloadResult> {
    if (!serverFileName) throw new TypeError('A server file name is required.');
    if (!localTargetPath) throw new TypeError('A local target path is required.');
    const fs = options?.fileSystem;
    if (!fs) throw new TypeError('A G9FileSystem is required.');
    const connection = resolveConnection(this._target);
    const signal = options.signal;
    const partialPath = localTargetPath + '.partial';
    const interrupted = (bytesWritten: number): G9FileDownloadResult => ({
      status: G9UploadStatus.Interrupted,
      bytesWritten,
      sha256: null,
      errorCode: null,
      errorMessage: null,
      localPath: null,
    });
    const failure = (code: string, message: string, bytesWritten: number): G9FileDownloadResult => ({
      status: G9UploadStatus.Failed,
      bytesWritten,
      sha256: null,
      errorCode: code,
      errorMessage: message,
      localPath: null,
    });

    for (let attempt = 0; ; attempt++) {
      throwIfAborted(signal);
      let existingPartial = (await fs.stat(partialPath))?.size ?? 0;

      let begin: G9BeginDownloadResult;
      try {
        await waitUntilConnected(connection, this._reconnectGraceMs, signal);
        begin = normalizeBeginDownloadResult(
          await raceAbort(connection.invoke<unknown>(this._beginMethod, serverFileName, existingPartial, this._chunkSize), signal),
        );
      } catch (error) {
        // As in .NET: a cancelled or finally failed begin call throws; only the transfer itself ends in a result.
        if (signal?.aborted) throw abortErrorFrom(signal);
        if (attempt < this._maxRetries) {
          await this._retry(attempt, error, existingPartial, false, signal);
          continue;
        }
        throw error;
      }

      if (begin.notFound) {
        return failure(G9DownloadErrorCodes.DownloadNotFound, `Server file '${serverFileName}' not found.`, existingPartial);
      }

      // Idempotent fast path: a fully-downloaded committed file that already matches the server's hash.
      if (this._shortCircuit && begin.sha256 && (await fs.stat(localTargetPath))) {
        const existingSha = await sha256Hex(await fileByteSource(fs, localTargetPath));
        if (existingSha === begin.sha256.toLowerCase()) {
          return {
            status: G9UploadStatus.Completed,
            bytesWritten: begin.totalBytes,
            sha256: begin.sha256,
            errorCode: null,
            errorMessage: null,
            localPath: localTargetPath,
          };
        }
      }

      // Stale partial larger than the server's file? Wipe and start over.
      if (existingPartial > begin.totalBytes) {
        await fs.remove(partialPath).catch(() => undefined);
        existingPartial = 0;
      }

      const chunkSize = begin.chunkSize > 0 ? begin.chunkSize : this._chunkSize;
      const started = monotonicNow();
      let bytesWritten = existingPartial;
      let pending: Uint8Array[] = [];
      let pendingBytes = 0;
      let writing: Promise<void> = Promise.resolve();
      let firstWrite = true; // drops whatever a crashed write left past the resume point
      const flush = (): Promise<void> => {
        if (pendingBytes === 0) return writing;
        const joined = new Uint8Array(pendingBytes);
        let at = 0;
        for (const part of pending) {
          joined.set(part, at);
          at += part.length;
        }
        const offset = bytesWritten - pendingBytes;
        const truncate = firstWrite;
        firstWrite = false;
        pending = [];
        pendingBytes = 0;
        writing = writing.then(() => fs.write(partialPath, offset, joined, truncate));
        return writing;
      };

      let streamError: unknown;
      try {
        await this._streamInto(connection, serverFileName, existingPartial, chunkSize, signal, (bytes) => {
          pending.push(bytes);
          pendingBytes += bytes.length;
          bytesWritten += bytes.length;
          if (pendingBytes >= FILE_BUFFER_BYTES) flush().catch(() => undefined);
          const elapsedMs = monotonicNow() - started;
          safeInvoke(options.onProgress, {
            fileName: serverFileName,
            bytesReceived: bytesWritten,
            totalBytes: begin.totalBytes,
            bytesPerSecond: elapsedMs > 0 ? ((bytesWritten - existingPartial) * 1000) / elapsedMs : 0,
            elapsedMs,
          });
        });
      } catch (error) {
        streamError = error;
      }
      // Whatever arrived is kept, so the next attempt (or the next call) resumes after it.
      try {
        await flush();
      } catch (error) {
        if (streamError === undefined) streamError = error;
        bytesWritten = (await fs.stat(partialPath).catch(() => null))?.size ?? existingPartial;
      }

      if (streamError !== undefined) {
        if (signal?.aborted) return interrupted(bytesWritten);
        if (attempt < this._maxRetries) {
          await this._retry(attempt, streamError, bytesWritten, false, signal);
          continue;
        }
        return failure(G9DownloadErrorCodes.DownloadFailed, describeError(streamError), bytesWritten);
      }

      // The stream ended early — the server hung up or the connection blipped. Keep the partial and resume.
      if (bytesWritten < begin.totalBytes) {
        if (attempt < this._maxRetries) {
          await this._retry(attempt, undefined, bytesWritten, true, signal);
          continue;
        }
        return interrupted(bytesWritten);
      }

      // Verify SHA-256, then commit atomically.
      const actualSha = await sha256Hex(await fileByteSource(fs, partialPath));
      if (!begin.sha256 || actualSha !== begin.sha256.toLowerCase()) {
        await fs.remove(partialPath).catch(() => undefined);
        return failure(G9DownloadErrorCodes.DownloadHashMismatch, `Server SHA-256 ${begin.sha256} != local ${actualSha}.`, bytesWritten);
      }

      let target = localTargetPath;
      if (await fs.stat(target)) {
        // Don't clobber an unrelated file — append a timestamp like the server does.
        const name = baseName(target);
        const dot = name.lastIndexOf('.');
        const stem = dot > 0 ? name.slice(0, dot) : name;
        const ext = dot > 0 ? name.slice(dot) : '';
        const dir = dirName(target);
        const separator = dir ? target.charAt(dir.length) : '';
        target = dir + separator + `${stem}.${utcStamp()}${ext}`;
      }
      await fs.move(partialPath, target);

      return {
        status: G9UploadStatus.Completed,
        bytesWritten,
        sha256: actualSha,
        errorCode: null,
        errorMessage: null,
        localPath: target,
      };
    }
  }

  private async _retry(
    attempt: number,
    error: unknown,
    bytesAlready: number,
    interrupted: boolean,
    signal: AbortSignal | undefined,
  ): Promise<void> {
    const backoffMs = transferBackoffMs(attempt);
    const info: G9UploadRetryInfo = {
      reason: interrupted ? 'interrupted' : 'attemptFailed',
      attempt,
      maxRetries: this._maxRetries,
      bytesAlreadyOnServer: bytesAlready,
      backoffMs,
    };
    if (!interrupted) info.error = error;
    safeInvoke(this._onRetry, info);
    await delay(backoffMs, signal); // a cancelled backoff throws, as .NET's BackoffAsync does

  }

  private _streamInto(
    connection: HubConnection,
    serverFileName: string,
    resumeFrom: number,
    chunkSize: number,
    signal: AbortSignal | undefined,
    onBytes: (bytes: Uint8Array) => void,
  ): Promise<void> {
    return new Promise<void>((resolve, reject) => {
      let done = false;
      let subscription: ISubscription<unknown> | undefined;

      const finish = (error?: unknown): void => {
        if (done) return;
        done = true;
        signal?.removeEventListener('abort', onAbort);
        if (error === undefined) resolve();
        else reject(error);
      };
      const onAbort = (): void => {
        subscription?.dispose();
        finish(abortErrorFrom(signal));
      };
      signal?.addEventListener('abort', onAbort, { once: true });

      try {
        subscription = connection.stream<unknown>(this._streamMethod, serverFileName, resumeFrom, chunkSize).subscribe({
          next: (item) => {
            if (done) return;
            try {
              const bytes = decodeChunk(item);
              if (bytes.length === 0) return;
              onBytes(bytes);
            } catch (error) {
              subscription?.dispose();
              finish(error);
            }
          },
          error: (error: unknown) => finish(error instanceof Error ? error : new Error(describeError(error))),
          complete: () => finish(),
        });
      } catch (error) {
        finish(error);
      }
    });
  }
}

/** `yyyyMMddHHmmssfff` in UTC (the .NET downloader's collision stamp). */
function utcStamp(): string {
  const now = new Date();
  const pad = (value: number, width: number): string => {
    let text = String(value);
    while (text.length < width) text = '0' + text;
    return text;
  };
  return (
    pad(now.getUTCFullYear(), 4) +
    pad(now.getUTCMonth() + 1, 2) +
    pad(now.getUTCDate(), 2) +
    pad(now.getUTCHours(), 2) +
    pad(now.getUTCMinutes(), 2) +
    pad(now.getUTCSeconds(), 2) +
    pad(now.getUTCMilliseconds(), 3)
  );
}
