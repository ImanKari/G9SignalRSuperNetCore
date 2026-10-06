// @g9tm/signalr-supernetcore-client — TypeScript twin of the G9SignalRSuperNetCore .NET client (2.10.1), for
// browsers, Node and Lynx (the Lynx platform layer is @g9tm/signalr-supernetcore-lynx).
// Parity rule: every change to the public surface of G9SignalRSuperNetCore.Client must be made here in the same
// change, and vice versa. See PARITY.md for the member-by-member mapping.

/** The package version (kept equal to the .NET library release it mirrors). */
export const VERSION = '2.10.1';

export {
  G9Client,
  buildHubConnection,
  type G9ClientOptions,
  type G9ConnectionFactoryContext,
  type G9ConnectionPhase,
  type G9ConnectionState,
  type G9LogLevel,
  type G9ReconnectedInfo,
  type G9Transport,
} from './client.js';

export {
  G9ReconnectPolicy,
  type G9ExponentialReconnectOptions,
  type G9ReconnectPolicyOptions,
} from './reconnectPolicy.js';

export { waitUntilConnected } from './waitUntilConnected.js';

export {
  authorize,
  normalizeAuthorizeResult,
  AUTHORIZE_METHOD,
  AUTHORIZE_RESULT_CALLBACK,
  type G9AuthorizeOptions,
  type G9AuthorizeResult,
} from './authorize.js';

export {
  G9ErrorCodes,
  G9DownloadErrorCodes,
  G9TimeoutError,
  getG9ErrorCode,
  isAbortError,
  type G9DownloadErrorCode,
  type G9ErrorCode,
} from './errors.js';

export { sha256Hex } from './sha256.js';

export { bytesSource, toByteSource, type G9ByteSource } from './byteSource.js';

export { fileByteSource, type G9FileStat, type G9FileSystem } from './fileSystem.js';

export { base64ToBytes, bytesToBase64 } from './internal/base64.js';

export { utf8Decode, utf8Encode } from './internal/utf8.js';

export {
  G9UploadStatus,
  G9TransferError,
  type G9BeginDownloadResult,
  type G9BeginUploadResult,
  type G9DownloadResult,
  type G9ServerUploadProgress,
  type G9UploadProgress,
  type G9UploadResult,
  type G9UploadRetryInfo,
  type G9UploadRetryReason,
} from './fileTransfer/dtos.js';

export {
  G9FileUploader,
  UPLOAD_PROGRESS_CALLBACK,
  type G9FileUploaderOptions,
  type G9UploadInput,
  type G9UploadOptions,
} from './fileTransfer/uploader.js';

export {
  G9FileDownloader,
  type G9DownloadOptions,
  type G9DownloadProgress,
  type G9DownloadToFileOptions,
  type G9FileDownloadResult,
  type G9FileDownloaderOptions,
} from './fileTransfer/downloader.js';

export {
  G9ConnectionQualityMonitor,
  type G9ConnectionQuality,
  type G9ConnectionQualityLevel,
  type G9ConnectionQualityMonitorOptions,
} from './qualityMonitor.js';

export type { G9ConnectionTarget } from './internal/target.js';
