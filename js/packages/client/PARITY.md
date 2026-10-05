# Parity: `G9SignalRSuperNetCore.Client` (.NET) ↔ `@g9tm/signalr-supernetcore-client` (TypeScript)

Release 2.10.0 on both sides. The Lynx platform layer (`@g9tm/signalr-supernetcore-lynx`) has its own contract — the
native module — mapped at the end of this file.

**Parity rule.** Every change to the public surface of `G9SignalRSuperNetCore.Client` (methods, options, reconnect
behaviour, error codes, file-transfer DTOs) must be made in this package in the same change, and vice versa. When a
member has no twin, this file says why. `test/parity.test.ts` checks error codes, the file-transfer and authorize DTO
property names, the upload status values and the quality-monitor thresholds and ping method against the .NET sources
mechanically.

Naming conventions of the twin: `PascalCase` members become `camelCase`; `TimeSpan` becomes a number of
milliseconds with an `Ms` suffix; `CancellationToken` becomes `AbortSignal`; `IProgress<T>` / events become
callbacks that return an unsubscribe function; enums the server serializes keep their numbers (`G9UploadStatus`),
client-only enums become string unions; `DateTime` becomes epoch milliseconds.

## `G9SignalRSuperNetCoreClient<TTarget, TServer, TListener>` ↔ `G9Client`

| .NET member | TypeScript | Notes |
| --- | --- | --- |
| `Connection` | `connection` | Same instance for the client's life. |
| `event StateChanged(G9DtConnectionState)` | `onState(cb)` + `state` | See the phase mapping below. |
| `Server` (typed proxy, source-generated) | n/a | The typed proxy is a .NET source-generator product. In TS call `invoke('Method', ...)` / `send(...)`; wrap them in a typed module in the app if wanted. |
| ctor `(serverUrl, customConfigureBuilder, configureHttpConnection)` | `new G9Client({ url, configureBuilder, accessTokenFactory, headers, transport, withCredentials })` | `configureHttpConnection` maps to the individual `IHttpConnectionOptions` fields. |
| `PrepareConnection(...)` (protected) | `buildHubConnection(options, retryPolicy)` | Exported helper; also used by `authorize`. |
| `RegisterListenerMethods()` (protected virtual) | `on(method, handler)` → unsubscribe | |
| `ConfigureConnectionOptions(G9DtClientConnectionOptions)` (protected virtual) | options `statefulReconnect`, `statefulReconnectBufferSize` | See `G9DtClientConnectionOptions`. |
| `ConnectAsync(ct)` | `start(signal)` | TS also retries the initial connect per the policy (see differences). |
| `StartConnectionAsync(ct)` (protected) | internal | |
| `DisconnectAsync(ct)` | `stop()` | |
| `DisposeAsync()` | `stop()` | Nothing else to release in JS. |
| — | `invoke`, `send`, `stream` | Thin delegates to `connection` (in .NET callers use `Connection` or the typed proxy). |
| — | `onReconnected(cb)`, `onClose(cb)` | .NET surfaces these through `StateChanged` (`Reconnected`, `Disconnected`). |
| — | `waitUntilConnected(timeoutMs, signal)` | Twin of `WaitUntilConnectedAsync` on `Connection`. |

### `G9EConnectionPhase` / `G9DtConnectionState` ↔ `G9ConnectionPhase` / `G9ConnectionState`

| .NET | TypeScript |
| --- | --- |
| `G9DtConnectionState.Phase` | `state.phase` |
| `G9DtConnectionState.UtcTimestamp` | `state.since` (epoch ms the phase was entered) |
| `G9DtConnectionState.Detail` | `state.connectionId` (connected) / `state.error` (failures) |
| `Connecting` | `'connecting'` (attempt 0) |
| `Connected` | `'connected'` |
| `ConnectFailed` | `'connecting'` with `attempt > 0`, `error` and `nextRetryInMs` (the client retries); `'disconnected'` with `error` when the policy gives up |
| `Reconnecting` | `'reconnecting'` with `attempt`, `nextRetryInMs`, `error` |
| `Reconnected` | `'connected'` + `onReconnected({ restarted: false })` |
| `Disconnected` | `'disconnected'` (+ `onClose(error)`); a final close while connected becomes `'reconnecting'` and a restart (`onReconnected({ restarted: true })`) |
| `TransportFallback` | n/a (no `WebSocketsFirst`, see below) |

## `G9DtClientConnectionOptions` ↔ `G9ClientOptions`

| .NET | TypeScript | Notes |
| --- | --- | --- |
| `UseStatefulReconnect` | `statefulReconnect` | Default differs: .NET `false`, TS `true` (see differences). |
| `StatefulReconnectBufferSize` | `statefulReconnectBufferSize` | |
| `WebSocketsFirst` | n/a | SignalR JS reads the transport options of a `HubConnection` once, so the "same connection, negotiate on fallback" behaviour cannot be reproduced without private APIs; and with stateful reconnect on (the TS default) .NET ignores it too. Pin `transport: 'websockets'` if needed. |
| (builder defaults) `Connection.ServerTimeout = 60 s` | `serverTimeoutMs` (default 30000), `keepAliveIntervalMs` (default 15000) | See differences. |
| — | `transport`, `headers`, `withCredentials`, `logLevel`, `protocol`, `reconnectPolicy`, `connectionFactory` | JS equivalents of builder/HTTP options; `connectionFactory` replaces the builder (tests). |
| `configureHttpConnection` (`HttpConnectionOptions.WebSocketFactory`, `SkipNegotiation`, `HttpMessageHandlerFactory`, `CloseTimeout`) | `webSocket`, `skipNegotiation`, `httpClient`, `httpTimeoutMs`, `eventSource` (2.10) | The transport knobs a runtime without a global WebSocket/`URL` needs (Lynx). `skipNegotiation` requires `transport: 'websockets'` and turns the stateful-reconnect default off (it is negotiated). |

## `G9SignalRSuperNetCoreClientWithJWTAuth` ↔ `authorize()` + `G9Client`

| .NET member | TypeScript | Notes |
| --- | --- | --- |
| ctor `(serverUrl, serverAuthUrl, jwToken, ...)` | `authorize(authHubUrl, data, options)` then `new G9Client({ url, accessTokenFactory })` | The auth exchange is a function, not a client subclass. |
| `AuthorizeAsync(authorizeData, ct)` | `authorize(authHubUrl, authorizeData, { signal, timeoutMs, headers, transport, withCredentials, logLevel })` | Same protocol: start, `send('Authorize', data)`, await the `AuthorizeResult` callback, stop. TS adds a timeout (default 30 s). |
| `IsAuthorized` | `result.isAccepted` | |
| `ConnectAsync()` (uses the token from `AuthorizeAsync` or the ctor) | `client.start()` with `accessTokenFactory: () => result.jwToken` | |
| `ConnectAsync(jwToken, ct)` | `new G9Client({ url, accessTokenFactory: () => jwToken }).start(signal)` | |
| hub method name `"Authorize"` / callback `"AuthorizeResult"` | `AUTHORIZE_METHOD` / `AUTHORIZE_RESULT_CALLBACK` | |

### `G9DtAuthorizeResult` ↔ `G9AuthorizeResult`

| .NET | TypeScript |
| --- | --- |
| `IsAccepted` | `isAccepted` |
| `RejectionReason` | `rejectionReason` (`"G9_RATE_LIMITED"` when the 2.9 auth throttle refused) |
| `JWToken` | `jwToken` |
| `ExtraData` | `extraData` |

## `G9CClientReconnectPolicy` ↔ `G9ReconnectPolicy`

| .NET member | TypeScript | Notes |
| --- | --- | --- |
| ctor `()` (200 ms × 2, cap 30 s, ±15%, give up after 5 min) | `G9ReconnectPolicy.exponential()` | Same curve. `new G9ReconnectPolicy()` is the TS client default (never gives up, see differences). |
| ctor `(baseDelay, factor, maxDelay, maxElapsed)` | `G9ReconnectPolicy.exponential({ baseDelayMs, factor, maxDelayMs, maxElapsedMs, jitter })` | `Timeout.InfiniteTimeSpan` ↔ `Infinity`. |
| `FromDelegate(Func<RetryContext, TimeSpan?>)` | `G9ReconnectPolicy.fromDelegate((ctx) => number \| null)` | |
| `NextRetryDelay(RetryContext)` | `nextRetryDelayInMilliseconds(RetryContext)` | SignalR JS `IRetryPolicy`. |
| — | ctor `({ delaysMs, maxDelayMs, jitter, maxAttempts, maxElapsedMs })`, `delayFor(attempt)` | Schedule-based policy used by default in TS. |

## `G9HubConnectionExtensions` ↔ functions

| .NET member | TypeScript |
| --- | --- |
| `WaitUntilConnectedAsync(connection, timeout, ct)` | `waitUntilConnected(connection, timeoutMs, signal)`; `G9Client.waitUntilConnected(timeoutMs, signal)` |
| `TimeoutException` | `G9TimeoutError` (`name === 'TimeoutError'`) |
| `OperationCanceledException` | `AbortError` |

Same semantics: returns at once when connected; otherwise completes on a reconnect or a 100 ms state poll; never
starts the connection; a negative timeout (other than infinite) is rejected.

## `G9CHttpResilience` ↔ n/a

| .NET member | TypeScript | Reason |
| --- | --- | --- |
| `CreateDefaultPipeline()`, `Apply(options, pipeline)`, `ApplyDefault(options)` | n/a | A Polly pipeline around .NET's negotiate `HttpClient`. In the browser the negotiate request is a `fetch` owned by SignalR JS; `G9Client`'s initial-connect retry loop covers transient negotiate failures instead. |

## `G9CConnectionQualityMonitor` ↔ `G9ConnectionQualityMonitor`

| .NET member | TypeScript |
| --- | --- |
| `DefaultPingMethod` (`"G9Ping"`) | `DEFAULT_PING_METHOD` |
| `GoodBelowMs` (150) | `GOOD_BELOW_MS` |
| `FairBelowMs` (400) | `FAIR_BELOW_MS` |
| `LostAfterFailures` (3) | `LOST_AFTER_FAILURES` |
| `DefaultInterval` (5 s) | `DEFAULT_INTERVAL_MS` (5000) |
| ctor `(connection, interval, pingMethod)` | `new G9ConnectionQualityMonitor(target, { intervalMs, pingMethod })` (target: `G9Client` or `HubConnection`) |
| `Current` | `current` |
| `event QualityChanged` | `onChange(cb)` → unsubscribe |
| `Start()` | `start()` |
| `DisposeAsync()` | `stop()` (TS can `start()` again afterwards) |
| — | `static classify(rttMs)`, `running` |
| `G9EConnectionQualityLevel` `Good`/`Fair`/`Poor`/`Lost` | `'good'`/`'fair'`/`'poor'`/`'lost'` |
| `G9DtConnectionQuality(RttMs, Level, MeasuredUtc)` | `{ rttMs, level, measuredAt }` (epoch ms; 0 = no measurement yet) |

Same behaviour: probe timeout = interval clamped to 1–10 s; reconnecting/closed → lost at once; reconnected → probe at
once (TS also on a `G9Client` restart); raised on level change only; the timestamp sent is an integer (the server
parameter is a `long`).

## `G9CFileUploader` ↔ `G9FileUploader`

| .NET member | TypeScript | Notes |
| --- | --- | --- |
| ctor `(connection, options)` | `new G9FileUploader(target, options)` | target: `G9Client` or `HubConnection`. |
| `UploadAsync(filePath, progress, serverAckProgress, ct)` | `uploadFile(filePath, { fileSystem, onProgress, serverAck, signal, … })` (2.10) | Exact twin over a `G9FileSystem` (`nodeFileSystem`, `lynxFileSystem()`): the file name, the .NET upload id, the same protocol. |
| — | `upload(file: Blob \| G9ByteSource \| bytes, { onProgress, serverAck, signal, fileName, uploadId, sha256 })` | Browsers have no path: a `Blob`/`File`, a byte source (2.10) or bytes. |
| `ComputeUploadId(FileInfo)` (private): SHA-256 of `lower(fullPath)\|length\|lastWriteTicks` | `uploadFile`: the same key (`G9FileStat` carries `fullPath` and `lastWriteTicks`); `static computeUploadId(name, size, lastModified)` for Blobs and byte sources | A file on disk gets the SAME id on both platforms since 2.10; a Blob has no path (see differences). |
| `ComputeSha256HexAsync` (private) | `sha256Hex(blob \| byteSource \| bytes, onProgress, signal)` (public) | Incremental, 4 MiB slices. |
| hub calls `BeginUpload(uploadId, fileName, totalBytes, chunkSize, sha256)` / `UploadChunks(uploadId, stream)` / callback `UploadProgress` | same; `UPLOAD_PROGRESS_CALLBACK` | Chunks: base64 strings under JSON, raw bytes under MessagePack. |
| retry backoff `min(30 s, 200 ms × 2^attempt)` on exception or `Interrupted` | same | Shared by uploader and downloader, as in .NET. |

### `G9DtUploadClientOptions` ↔ `G9FileUploaderOptions`

| .NET | TypeScript | Notes |
| --- | --- | --- |
| `ChunkSize` (64 KiB) | `chunkSize` (64 KiB) | |
| `MaxRetries` (5) | `maxRetries` (5) | |
| `BeginMethod` (`"BeginUpload"`) | `beginMethod` | |
| `UploadMethod` (`"UploadChunks"`) | `uploadMethod` | |
| `OnRetry` | `onRetry` | |
| `ReconnectGrace` (2 min) | `reconnectGraceMs` (120000) | Both wait up to this long for `Connected` before each `BeginUpload` (.NET since 2.9, through `WaitUntilConnectedAsync`), then fail with `TimeoutException` / `TimeoutError`. |
| — | `maxChunksInFlight` (8) | JS SignalR has no stream backpressure; this bounds memory. |

### DTOs

| .NET | TypeScript | Fields |
| --- | --- | --- |
| `G9DtBeginUploadResult` | `G9BeginUploadResult` | `uploadId`, `bytesAlreadyReceived`, `chunkSize`, `alreadyCompleted`, `finalPath`, `storedFileName` (2.9) |
| `G9DtUploadResult` | `G9UploadResult` | `status`, `bytesWritten`, `sha256`, `finalPath`, `storedFileName` (2.9), `errorCode`, `errorMessage` |
| `G9EUploadStatus` `Completed`/`Interrupted`/`Failed` | `G9UploadStatus` `0`/`1`/`2` | Numbers as the server sends them; names are accepted too. |
| `G9DtUploadProgress` (server push) | `G9ServerUploadProgress` | `uploadId`, `bytesReceived`, `totalBytes` |
| `G9DtUploadClientProgress(UploadId, BytesSent, BytesAcknowledged, TotalBytes, BytesPerSecond, Elapsed)` | `G9UploadProgress` | `uploadId`, `bytesSent`, `bytesAcknowledged`, `totalBytes`, `bytesPerSecond`, `elapsedMs`, + `phase` (`'hashing'` \| `'uploading'`) |
| `G9EUploadRetryReason` `AttemptFailed`/`Interrupted` | `G9UploadRetryReason` `'attemptFailed'`/`'interrupted'` | |
| `G9DtUploadRetryInfo(Reason, Attempt, MaxRetries, BytesAlreadyOnServer, Backoff, Exception)` | `G9UploadRetryInfo` | `reason`, `attempt`, `maxRetries`, `bytesAlreadyOnServer`, `backoffMs`, `error` |

The normalizers accept camelCase (JSON) and PascalCase (MessagePack) property names.

## `G9CFileDownloader` ↔ `G9FileDownloader`

| .NET member | TypeScript | Notes |
| --- | --- | --- |
| ctor `(connection, options)` | `new G9FileDownloader(target, options)` | |
| `DownloadAsync(serverFileName, localTargetPath, progress, ct)` → `G9DtDownloadResult` | `downloadToFile(serverFileName, localTargetPath, { fileSystem, onProgress, signal })` → `G9FileDownloadResult` (2.10) | Exact twin: `<target>.partial` resumed across calls, `ShortCircuitWhenComplete`, SHA-256 check, timestamped name when the target exists, `Interrupted` on cancel, results for transfer failures; a cancelled or finally failed `BeginDownload` throws (as in .NET). |
| — | `download(serverFileName, { signal, onProgress, chunkSize, type })` → `Blob`; `downloadBytes(…)` → `Uint8Array` (2.10) | In memory (browsers, Lynx): a failure throws `G9TransferError` (`code`, `status`, `bytesWritten`, `toResult()`). |
| hub calls `BeginDownload(fileName, resumeFrom, chunkSize)` / stream `DownloadChunks(fileName, resumeFrom, chunkSize)` | same | |
| `G9_DOWNLOAD_HASH_MISMATCH`, `G9_DOWNLOAD_NOT_FOUND`, `G9_DOWNLOAD_FAILED` (private constants) | `G9DownloadErrorCodes` | |

| `G9DtDownloadClientOptions` | `G9FileDownloaderOptions` | Notes |
| --- | --- | --- |
| `ChunkSize`, `MaxRetries`, `BeginMethod` (`"BeginDownload"`), `StreamMethod` (`"DownloadChunks"`), `OnRetry` | `chunkSize`, `maxRetries`, `beginMethod`, `streamMethod`, `onRetry` | |
| `ShortCircuitWhenComplete` | `shortCircuitWhenComplete` (2.10) | Used by `downloadToFile`. |
| — | `verifySha256` (true), `reconnectGraceMs` (120000) | |

| DTO | TypeScript | Fields |
| --- | --- | --- |
| `G9DtBeginDownloadResult` | `G9BeginDownloadResult` | `notFound`, `totalBytes`, `sha256`, `chunkSize`, `resumeFrom` |
| `G9DtDownloadResult` | `G9DownloadResult` (via `G9TransferError.toResult()`) | `status`, `bytesWritten`, `sha256`, `errorCode`, `errorMessage`; `LocalPath` n/a |
| `G9DtDownloadClientProgress(FileName, BytesReceived, TotalBytes, BytesPerSecond, Elapsed)` | `downloadToFile`: `onProgress({ fileName, bytesReceived, totalBytes, bytesPerSecond, elapsedMs })` (`G9DownloadProgress`); in memory: `onProgress(received, total)` | |

## `G9CErrorCodes` (server) ↔ `G9ErrorCodes`

Every constant, same name, same value: `RateLimited` (`G9_RATE_LIMITED`), `ConnectionLimit`, `ConnectionRequired`,
`RoleRequired`, `ClaimRequired`, `UploadTooLarge`, `UploadHashMismatch`, `UploadUnknownId`, `UploadFailed`,
`UploadNameConflict`, `UploadMetadataConflict`, `PermissionRequired` (2.9, `G9_PERMISSION_REQUIRED`),
`UploadForbidden` (2.9, `G9_UPLOAD_FORBIDDEN`). TS adds `getG9ErrorCode(error)` to read a code out of a rejected
invocation (`HubException` message), a result or a `G9TransferError`.

## Other .NET client assemblies

| .NET | TypeScript |
| --- | --- |
| `G9SignalRSuperNetCore.Client.MessagePack` (`AddG9MessagePackProtocol`, wire shapes) | `new G9Client({ protocol: new MessagePackHubProtocol() })` from `@microsoft/signalr-protocol-msgpack`; the transfer classes detect the binary protocol. JS needs no shapes. |
| Server hub method `G9Ping(long)` (2.9) | Used by `G9ConnectionQualityMonitor`. |

## TypeScript-only exports

| Export | Why there is no .NET twin |
| --- | --- |
| `VERSION` (`'2.9.0'`) | .NET reads its version from the assembly. Kept equal to `G9PackageVersion`. |
| `normalizeAuthorizeResult(raw)` | Reads `G9DtAuthorizeResult` in either property casing; .NET binds the type directly. |
| `isAbortError(error)` | JS has no `OperationCanceledException` type to catch. |
| `G9ConnectionTarget` (type) | The transfer classes and the monitor accept a `G9Client` or a bare `HubConnection`; .NET takes a `HubConnection`. |
| `G9ByteSource`, `bytesSource`, `toByteSource`, `fileByteSource` (2.10) | .NET reads files and streams; JS needs one shape for Blob, memory and platform files (Lynx has no Blob). |
| `G9FileSystem`, `G9FileStat`, `nodeFileSystem` (`./node`) (2.10) | .NET uses `System.IO`; JS runtimes need a pluggable file layer (Node `fs`, the Lynx native module). |
| `bytesToBase64`, `base64ToBytes`, `utf8Encode`, `utf8Decode` (2.10) | .NET has `Convert`/`Encoding`; Lynx has no `btoa`/`atob`/`TextEncoder`/`TextDecoder`. |

## Deliberate differences (keep in mind when changing either side)

1. **Default reconnect policy.** TS `G9Client` never gives up (0, 1, 2, 5, 10, 20 s, then 30 s, ±20% jitter), because
   the SPA must come back by itself after any outage. .NET uses `G9CClientReconnectPolicy` (200 ms × 2, cap 30 s,
   ±15%, gives up after 5 min). `G9ReconnectPolicy.exponential()` reproduces the .NET curve.
2. **Initial connect and restart.** `G9Client.start()` retries the initial connect per the policy and restarts after a
   final close; .NET `ConnectAsync` tries once and a final close stays closed (`Disconnected`).
3. **Stateful reconnect** is on by default in TS (`statefulReconnect: true`), off in .NET (`UseStatefulReconnect`).
4. **Server timeout:** TS 30 s (`serverTimeoutMs`), .NET 60 s (`Connection.ServerTimeout`). Keep-alive 15 s on both.
5. **Upload id key:** a file on disk (`uploadFile`) uses the .NET key `lower(fullPath)|length|lastWriteTicks` on both
   platforms (2.10). A Blob or byte source has no path and uses `name|size|lastModified` (ms) — only those differ.
6. **Waiting for the connection in downloads:** both uploaders wait up to `ReconnectGrace` / `reconnectGraceMs`
   (2 min) for the connection before every `BeginUpload` (.NET since 2.9). The TS downloader waits the same way
   before every `BeginDownload` (`reconnectGraceMs`); the .NET `G9CFileDownloader` has no such option and instead
   retries a failed `BeginDownload` with the transfer backoff.
7. **In-memory downloads** (`download()` → Blob, `downloadBytes()` → bytes) resume within one call (bytes kept in
   memory), and throw `G9TransferError` instead of returning a failed result. `downloadToFile()` (2.10) has none of these
   differences: it is the .NET behaviour.
8. **No `WebSocketsFirst`** (see above).

## The Lynx platform layer (`@g9tm/signalr-supernetcore-lynx`)

.NET has no counterpart of this package: a .NET client gets its WebSocket and files from the BCL. On Lynx they come from
the `G9SignalRLynxModule` native module, whose contract is `types/g9-signalr-lynx-module.d.ts`:

| Contract method | Android (`android/…/core`) | iOS (`ios/src/core`) | Lynxtron (`src/lynxtron`) | Used by |
| --- | --- | --- | --- | --- |
| `wsOpen` / `wsSendText` / `wsSendBinary` / `wsClose` / `wsPoll` | `G9SocketCore` (OkHttp 4.12) | `G9SignalRSocketCore` (`NSURLSessionWebSocketTask`, 64 MiB messages) | Node's `WebSocket` (or `ws`) | `G9LynxWebSocket` → the `webSocket` option |
| `fileStat` / `fileRead` / `fileWrite` / `fileMove` / `fileDelete` | `G9FileCore` (`java.io` + `java.nio.file`) | `G9SignalRFileCore` (Foundation) | `nodeFileSystem` | `lynxFileSystem()` → `uploadFile`, `downloadToFile` |
| `capabilities` | `G9SignalRLynxModule` | `G9SignalRLynxModule` | bridge | diagnostics |

`fileStat` reports .NET `FileInfo` facts (`FullName`, `Length`, `LastWriteTimeUtc.Ticks` as a decimal string), so the
upload id of a file on Lynx is the .NET one. A change to the contract is made on all three platforms and in
`src/native.ts` together (the repository's AGENTS.md).
