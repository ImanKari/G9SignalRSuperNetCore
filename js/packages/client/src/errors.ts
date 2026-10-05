/**
 * Twin of the server's `G9SignalRSuperNetCore.Server.Classes.Errors.G9CErrorCodes`: the stable error codes a G9 hub
 * policy rejects an invocation with. The server wraps them in a `HubException`, so on the client they appear inside the
 * error message of the failed `invoke` (use {@link getG9ErrorCode} to pull them out). Same names, same values.
 */
export const G9ErrorCodes = Object.freeze({
  /** The caller exceeded a rate limit (`G9AttrRateLimit`), or the JWT authorize route throttled it (2.9). */
  RateLimited: 'G9_RATE_LIMITED',
  /** The caller exceeded the per-user or per-IP connection limit (`G9AttrConnectionLimit`). */
  ConnectionLimit: 'G9_CONNECTION_LIMIT',
  /** The connection is not in the `Connected` state for a method that requires it (`G9AttrConnectionRequired`). */
  ConnectionRequired: 'G9_CONNECTION_REQUIRED',
  /** The authenticated principal does not carry the required role (`G9AttrRequireRole`). */
  RoleRequired: 'G9_ROLE_REQUIRED',
  /** The authenticated principal does not carry the required claim value (`G9AttrRequireClaim`). */
  ClaimRequired: 'G9_CLAIM_REQUIRED',
  /** A file upload exceeded the configured maximum size. */
  UploadTooLarge: 'G9_UPLOAD_TOO_LARGE',
  /** A file upload's declared SHA-256 did not match the bytes the server received. */
  UploadHashMismatch: 'G9_UPLOAD_HASH_MISMATCH',
  /** A file upload referenced an unknown or already-completed upload id. */
  UploadUnknownId: 'G9_UPLOAD_UNKNOWN_ID',
  /** A file upload failed for another reason (I/O, cancellation, ...). */
  UploadFailed: 'G9_UPLOAD_FAILED',
  /** A different file is already committed under the requested name (same name, other bytes). */
  UploadNameConflict: 'G9_UPLOAD_NAME_CONFLICT',
  /** An upload id was resumed with metadata describing different content than it was begun with. */
  UploadMetadataConflict: 'G9_UPLOAD_METADATA_CONFLICT',
  /** The caller lacks an application-defined permission (`G9AttrRequirePermission`) (2.9). */
  PermissionRequired: 'G9_PERMISSION_REQUIRED',
  /** The server's upload `Authorize` hook refused a file-transfer operation (2.9). */
  UploadForbidden: 'G9_UPLOAD_FORBIDDEN',
} as const);

/** One of the {@link G9ErrorCodes} values. */
export type G9ErrorCode = (typeof G9ErrorCodes)[keyof typeof G9ErrorCodes];

/**
 * Twin of the private constants of the .NET `G9CFileDownloader`: the codes a failed download reports. They are
 * produced on the client (the server never sends them), which is why they are not part of {@link G9ErrorCodes}.
 */
export const G9DownloadErrorCodes = Object.freeze({
  /** The assembled bytes did not hash to the SHA-256 the server announced. */
  DownloadHashMismatch: 'G9_DOWNLOAD_HASH_MISMATCH',
  /** The server has no file under the requested name. */
  DownloadNotFound: 'G9_DOWNLOAD_NOT_FOUND',
  /** The download failed after the retry budget was spent. */
  DownloadFailed: 'G9_DOWNLOAD_FAILED',
} as const);

/** One of the {@link G9DownloadErrorCodes} values. */
export type G9DownloadErrorCode = (typeof G9DownloadErrorCodes)[keyof typeof G9DownloadErrorCodes];

const CODE_PATTERN = /\bG9_[A-Z0-9_]+\b/;

/**
 * Returns the G9 error code carried by `error`, or `null`. Works on the errors a rejected `invoke` produces (the
 * server's `HubException` message ends up in `error.message`, e.g.
 * `"An unexpected error occurred invoking 'Send' on the server. HubException: G9_RATE_LIMITED"`), on
 * {@link G9TransferError}, on upload results (`errorCode`) and on plain strings.
 */
export function getG9ErrorCode(error: unknown): string | null {
  if (error == null) return null;
  if (typeof error === 'string') return CODE_PATTERN.exec(error)?.[0] ?? null;
  if (typeof error === 'object') {
    const record = error as { code?: unknown; errorCode?: unknown; message?: unknown; rejectionReason?: unknown };
    for (const candidate of [record.code, record.errorCode, record.rejectionReason, record.message]) {
      if (typeof candidate === 'string') {
        const match = CODE_PATTERN.exec(candidate);
        if (match) return match[0];
      }
    }
  }
  return null;
}

/** Thrown by `waitUntilConnected` / `authorize` when the time budget runs out. `name` is `'TimeoutError'`. */
export class G9TimeoutError extends Error {
  override readonly name = 'TimeoutError';

  constructor(message: string) {
    super(message);
  }
}

/** Creates the error an aborted operation rejects with: the signal's reason when it is an Error, else an `AbortError`. */
export function abortErrorFrom(signal?: AbortSignal, message = 'The operation was aborted.'): Error {
  const reason: unknown = signal?.reason;
  if (reason instanceof Error) return reason;
  return createAbortError(message);
}

/** A `DOMException` named `AbortError` where available (browsers, Node 17+), otherwise an Error with that name. */
export function createAbortError(message = 'The operation was aborted.'): Error {
  if (typeof DOMException === 'function') return new DOMException(message, 'AbortError');
  const error = new Error(message);
  error.name = 'AbortError';
  return error;
}

/** True when `error` is an abort (a `DOMException`/Error named `AbortError`). */
export function isAbortError(error: unknown): boolean {
  return typeof error === 'object' && error !== null && (error as { name?: unknown }).name === 'AbortError';
}
