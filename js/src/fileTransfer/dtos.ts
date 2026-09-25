// Twins of the file-transfer DTOs of G9SignalRSuperNetCore (server: Classes/FileUpload/*, client: FileUpload/*).
// The server serializes enums as numbers (no JsonStringEnumConverter), with camelCase property names under JSON and
// declared (PascalCase) names under MessagePack; the normalizers below accept both casings and both enum forms.

/**
 * Twin of `G9EUploadStatus` — also the status of downloads, as in .NET. The values are the numbers the server sends.
 */
export const G9UploadStatus = Object.freeze({
  /** The upload completed and the file is committed. */
  Completed: 0,
  /** The chunk stream ended early; the server kept the partial for a resume. */
  Interrupted: 1,
  /** The upload failed and the partial was deleted. */
  Failed: 2,
} as const);

/** One of the {@link G9UploadStatus} values (0 Completed, 1 Interrupted, 2 Failed). */
export type G9UploadStatus = (typeof G9UploadStatus)[keyof typeof G9UploadStatus];

/** Twin of the server's `G9DtBeginUploadResult`. */
export interface G9BeginUploadResult {
  uploadId: string;
  /** Bytes already on the server for this upload (0 on first start, more on resume). */
  bytesAlreadyReceived: number;
  /** The chunk size the server expects (the client's request unless capped). */
  chunkSize: number;
  /** True when the upload was already committed by an earlier call (idempotent retry). */
  alreadyCompleted: boolean;
  /** Server-side path of the committed file when `alreadyCompleted`. */
  finalPath: string | null;
  /** 2.9: committed file name relative to the upload root when `alreadyCompleted` (null from older servers). */
  storedFileName: string | null;
}

/** Twin of the server's `G9DtUploadResult`. */
export interface G9UploadResult {
  status: G9UploadStatus;
  /** Total bytes written (the declared length on success). */
  bytesWritten: number;
  /** Lower-case hex SHA-256 of the committed file (Completed). */
  sha256?: string | null;
  /** Server-side path of the committed file (Completed). */
  finalPath?: string | null;
  /**
   * 2.9: the committed file name relative to the server upload root (Completed) — the name to pass to
   * {@link G9FileDownloader.download}. It differs from the local name when the server randomizes committed names or
   * the name was taken. Null from servers older than 2.9.
   */
  storedFileName?: string | null;
  /** Stable G9 error code (Failed), e.g. `G9_UPLOAD_HASH_MISMATCH`, `G9_UPLOAD_FORBIDDEN`. */
  errorCode?: string | null;
  /** Human-readable error (Failed). */
  errorMessage?: string | null;
}

/** Twin of the server-pushed `G9DtUploadProgress` (the `UploadProgress` client callback). */
export interface G9ServerUploadProgress {
  uploadId: string;
  /** Bytes the server has durably written so far. */
  bytesReceived: number;
  /** Declared total (the sample hub sends 0: "use yours"). */
  totalBytes: number;
}

/** Client-side upload progress. Twin of `G9DtUploadClientProgress` (+ `phase`). */
export interface G9UploadProgress {
  uploadId: string;
  /**
   * Bytes handed to SignalR so far (counting the resume offset). During `phase: 'hashing'` it counts the bytes hashed.
   */
  bytesSent: number;
  /** Bytes the server confirmed durable (from its `UploadProgress` callback). */
  bytesAcknowledged: number;
  /** File size. */
  totalBytes: number;
  /** Throughput of the current attempt (or of the hashing). */
  bytesPerSecond: number;
  /** Time since the current attempt (or the hashing) began. */
  elapsedMs: number;
  /** `'hashing'` while the SHA-256 of the file is computed, then `'uploading'`. */
  phase: 'hashing' | 'uploading';
}

/** Why a retry is scheduled. Twin of `G9EUploadRetryReason` (AttemptFailed, Interrupted). */
export type G9UploadRetryReason = 'attemptFailed' | 'interrupted';

/** Passed to `onRetry` of the uploader and the downloader. Twin of `G9DtUploadRetryInfo`. */
export interface G9UploadRetryInfo {
  reason: G9UploadRetryReason;
  /** Zero-based index of the attempt that just ended. */
  attempt: number;
  /** The configured retry budget. */
  maxRetries: number;
  /** Byte offset the next attempt resumes from (uploads: on the server; downloads: received so far). */
  bytesAlreadyOnServer: number;
  /** Delay before the next attempt. */
  backoffMs: number;
  /** The error that ended the attempt (undefined for `interrupted`). */
  error?: unknown;
}

/** Twin of the server's `G9DtBeginDownloadResult`. */
export interface G9BeginDownloadResult {
  /** True when the requested file does not exist on the server. */
  notFound: boolean;
  /** Total size of the file. */
  totalBytes: number;
  /** Lower-case hex SHA-256 of the file (may be empty). */
  sha256: string;
  /** Server-suggested chunk size. */
  chunkSize: number;
  /** The resume offset the client asked for (echoed). */
  resumeFrom: number;
}

/** Twin of `G9DtDownloadResult`, carried by {@link G9TransferError} when a download fails. */
export interface G9DownloadResult {
  status: G9UploadStatus;
  bytesWritten: number;
  sha256?: string | null;
  errorCode?: string | null;
  errorMessage?: string | null;
}

/** Thrown by {@link G9FileDownloader.download} when a download does not complete. */
export class G9TransferError extends Error {
  override readonly name = 'G9TransferError';
  /** A {@link G9DownloadErrorCodes} value, or null for an interrupted transfer. */
  readonly code: string | null;
  /** Failed or Interrupted. */
  readonly status: G9UploadStatus;
  /** Bytes received before the failure. */
  readonly bytesWritten: number;

  constructor(code: string | null, message: string, status: G9UploadStatus, bytesWritten: number, cause?: unknown) {
    super(message, cause === undefined ? undefined : { cause });
    this.code = code;
    this.status = status;
    this.bytesWritten = bytesWritten;
  }

  /** The .NET-shaped result this error stands for. */
  toResult(): G9DownloadResult {
    return {
      status: this.status,
      bytesWritten: this.bytesWritten,
      sha256: null,
      errorCode: this.code,
      errorMessage: this.message,
    };
  }
}

// ---- normalizers ---------------------------------------------------------------------------------------------------

type Loose = Record<string, unknown>;

function asRecord(raw: unknown): Loose {
  return typeof raw === 'object' && raw !== null ? (raw as Loose) : {};
}

function field(record: Loose, camel: string): unknown {
  if (camel in record) return record[camel];
  const pascal = camel.charAt(0).toUpperCase() + camel.slice(1);
  return record[pascal];
}

function num(value: unknown): number {
  if (typeof value === 'number') return value;
  if (typeof value === 'bigint') return Number(value);
  if (typeof value === 'string' && value.trim() !== '' && Number.isFinite(Number(value))) return Number(value);
  return 0;
}

function strOrNull(value: unknown): string | null {
  return typeof value === 'string' ? value : null;
}

/** Reads a `G9EUploadStatus` sent as a number (default) or a name (string enum converter). */
export function toUploadStatus(value: unknown): G9UploadStatus {
  if (value === 0 || value === 1 || value === 2) return value;
  if (typeof value === 'string') {
    switch (value.trim().toLowerCase()) {
      case 'completed':
      case '0':
        return G9UploadStatus.Completed;
      case 'interrupted':
      case '1':
        return G9UploadStatus.Interrupted;
      case 'failed':
      case '2':
        return G9UploadStatus.Failed;
    }
  }
  return G9UploadStatus.Failed;
}

export function normalizeBeginUploadResult(raw: unknown): G9BeginUploadResult {
  const r = asRecord(raw);
  return {
    uploadId: strOrNull(field(r, 'uploadId')) ?? '',
    bytesAlreadyReceived: num(field(r, 'bytesAlreadyReceived')),
    chunkSize: num(field(r, 'chunkSize')),
    alreadyCompleted: field(r, 'alreadyCompleted') === true,
    finalPath: strOrNull(field(r, 'finalPath')),
    storedFileName: strOrNull(field(r, 'storedFileName')),
  };
}

export function normalizeUploadResult(raw: unknown): G9UploadResult {
  const r = asRecord(raw);
  return {
    status: toUploadStatus(field(r, 'status')),
    bytesWritten: num(field(r, 'bytesWritten')),
    sha256: strOrNull(field(r, 'sha256')),
    finalPath: strOrNull(field(r, 'finalPath')),
    storedFileName: strOrNull(field(r, 'storedFileName')),
    errorCode: strOrNull(field(r, 'errorCode')),
    errorMessage: strOrNull(field(r, 'errorMessage')),
  };
}

export function normalizeServerUploadProgress(raw: unknown): G9ServerUploadProgress | null {
  const r = asRecord(raw);
  const uploadId = strOrNull(field(r, 'uploadId'));
  if (uploadId === null) return null;
  return { uploadId, bytesReceived: num(field(r, 'bytesReceived')), totalBytes: num(field(r, 'totalBytes')) };
}

export function normalizeBeginDownloadResult(raw: unknown): G9BeginDownloadResult {
  const r = asRecord(raw);
  return {
    notFound: field(r, 'notFound') === true,
    totalBytes: num(field(r, 'totalBytes')),
    sha256: strOrNull(field(r, 'sha256')) ?? '',
    chunkSize: num(field(r, 'chunkSize')),
    resumeFrom: num(field(r, 'resumeFrom')),
  };
}

/** Exponential backoff shared with .NET: `min(30000, 200 * 2^attempt)` ms. */
export function transferBackoffMs(attempt: number): number {
  return Math.min(30000, 200 * Math.pow(2, attempt));
}
