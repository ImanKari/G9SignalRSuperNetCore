import type { HubConnection, ISubscription } from '@microsoft/signalr';
import { abortErrorFrom, G9DownloadErrorCodes } from '../errors.js';
import { delay, describeError, raceAbort, safeInvoke, throwIfAborted } from '../internal/async.js';
import { base64ToBytes } from '../internal/base64.js';
import { resolveConnection, type G9ConnectionTarget } from '../internal/target.js';
import { Sha256 } from '../sha256.js';
import { waitUntilConnected } from '../waitUntilConnected.js';
import {
  G9TransferError,
  G9UploadStatus,
  normalizeBeginDownloadResult,
  transferBackoffMs,
  type G9BeginDownloadResult,
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
  /** Verify the assembled bytes against the SHA-256 the server announces. Default true. */
  verifySha256?: boolean;
  /** How long an attempt waits for the connection to be connected again before its begin call. Default 120000 ms. */
  reconnectGraceMs?: number;
}

/** Per-call options of {@link G9FileDownloader.download}. */
export interface G9DownloadOptions {
  /** Cancels the download (rejects with an `AbortError`). */
  signal?: AbortSignal;
  /** Progress sink: bytes received so far and the file size. */
  onProgress?: (received: number, total: number) => void;
  /** Chunk size for this call (overrides the constructor option). */
  chunkSize?: number;
  /** MIME type of the resulting Blob. */
  type?: string;
}

/** Accumulated bytes flushed into Blob parts every 4 MiB, so large downloads can be paged out by the browser. */
const FLUSH_BYTES = 4 * 1024 * 1024;

class ChunkStore {
  private _blobs: Blob[] = [];
  private _pending: Uint8Array[] = [];
  private _pendingBytes = 0;
  private _hash = new Sha256();
  size = 0;

  append(bytes: Uint8Array): void {
    this._hash.update(bytes);
    this._pending.push(bytes);
    this._pendingBytes += bytes.length;
    this.size += bytes.length;
    if (this._pendingBytes >= FLUSH_BYTES) this._flush();
  }

  reset(): void {
    this._blobs = [];
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
    return new Blob(this._blobs, type ? { type } : undefined);
  }

  private _flush(): void {
    if (this._pending.length === 0) return;
    this._blobs.push(new Blob(this._pending as BlobPart[]));
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
 * Resumable file downloader over SignalR server-to-client streaming, assembling a Blob. Twin of the .NET
 * `G9CFileDownloader`: `BeginDownload(fileName, resumeFrom, chunkSize)` then the `DownloadChunks(fileName, resumeFrom,
 * chunkSize)` stream; a failed or interrupted stream is retried with the .NET backoff and resumes from the bytes
 * already received (in memory — there is no `.partial` file in a browser). The result is verified against the
 * server's SHA-256 (hashed incrementally as chunks arrive).
 *
 * Failures reject with a {@link G9TransferError} carrying the .NET error code (`G9_DOWNLOAD_NOT_FOUND`,
 * `G9_DOWNLOAD_HASH_MISMATCH`, `G9_DOWNLOAD_FAILED`) and status.
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

    if (!Number.isInteger(this._chunkSize) || this._chunkSize <= 0) throw new RangeError('chunkSize must be a positive integer.');
    if (!Number.isInteger(this._maxRetries) || this._maxRetries < 0) throw new RangeError('maxRetries must be a non-negative integer.');
  }

  /** Downloads `serverFileName` (e.g. an upload's `storedFileName`) and resolves with its content. */
  async download(serverFileName: string, options: G9DownloadOptions = {}): Promise<Blob> {
    if (!serverFileName) throw new TypeError('A server file name is required.');
    const connection = resolveConnection(this._target);
    const signal = options.signal;
    const requestedChunk = options.chunkSize ?? this._chunkSize;
    if (!Number.isInteger(requestedChunk) || requestedChunk <= 0) throw new RangeError('chunkSize must be a positive integer.');
    const store = new ChunkStore();

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
        await this._streamInto(connection, serverFileName, store, begin.totalBytes, chunkSize, signal, options.onProgress);
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

      return store.toBlob(options.type);
    }
  }

  private _streamInto(
    connection: HubConnection,
    serverFileName: string,
    store: ChunkStore,
    totalBytes: number,
    chunkSize: number,
    signal: AbortSignal | undefined,
    onProgress: ((received: number, total: number) => void) | undefined,
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
        subscription = connection.stream<unknown>(this._streamMethod, serverFileName, store.size, chunkSize).subscribe({
          next: (item) => {
            if (done) return;
            try {
              const bytes = decodeChunk(item);
              if (bytes.length === 0) return;
              store.append(bytes);
              safeInvoke(onProgress, store.size, totalBytes);
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
