# @g9tm/signalr-supernetcore-client

TypeScript twin of the **G9SignalRSuperNetCore** .NET client (release **2.10.1**) for browsers (SvelteKit SPA,
Capacitor WebView, Tauri WebView), Node 18+ and **Lynx** (ReactLynx apps: Android, iOS, Windows, macOS, Web — add
[`@g9tm/signalr-supernetcore-lynx`](../lynx/README.md) for the native WebSocket and file access Lynx lacks).

It wraps [`@microsoft/signalr`](https://www.npmjs.com/package/@microsoft/signalr) and adds what the .NET client
library adds on top of a bare `HubConnection`:

| Feature | Export |
| --- | --- |
| Resilient connection: initial-connect retry, SignalR automatic reconnect, restart after a final close, one state stream | `G9Client` |
| Reconnect policy with jitter (never gives up by default) | `G9ReconnectPolicy` |
| JWT authorize route (`G9GetJwtHub`) | `authorize()` |
| Resumable upload over client-to-server streaming (SHA-256 verified, bounded memory) | `G9FileUploader` |
| Resumable download over server-to-client streaming (SHA-256 verified) | `G9FileDownloader` |
| Round-trip quality indicator (`G9Ping`) | `G9ConnectionQualityMonitor` |
| Wait for the connection after a network change | `waitUntilConnected()` / `client.waitUntilConnected()` |
| Stable server error codes | `G9ErrorCodes`, `getG9ErrorCode()` |
| Incremental SHA-256 of Blobs and byte sources of any size | `sha256Hex()` |
| Bytes from anywhere: Blob/File, memory, a file a platform reads | `G9ByteSource`, `bytesSource()`, `toByteSource()` |
| Files on disk, the .NET way (`.partial` resume, .NET upload id) | `G9FileSystem`, `uploadFile()`, `downloadToFile()`, `nodeFileSystem` (`./node`) |
| base64 / UTF-8 without `btoa`, `atob`, `TextEncoder`, `TextDecoder` | `bytesToBase64()`, `base64ToBytes()`, `utf8Encode()`, `utf8Decode()` |

ESM only, TypeScript types included, no runtime dependency besides the `@microsoft/signalr` peer (^10). The shipped code
is ES2019 and uses no API Lynx's background thread lacks (no `BigInt`, `Intl`, `TextEncoder`, `URL`, `Blob` unless the
runtime has one); a Lynx-shaped sandbox run is part of the test suite.

## Install

`@microsoft/signalr` ^10 is a peer dependency; install it next to this package. The repository's release pipeline
type-checks, tests, builds and packs this package with every release and publishes it to npm (scope `@g9tm`) when the
pipeline has an `NpmToken`:

```bash
npm install @microsoft/signalr@^10 @g9tm/signalr-supernetcore-client@2.10.1

# optional, for binary frames:
npm install @microsoft/signalr-protocol-msgpack@^10

# or from a tarball (see below)
npm install ./vendor/g9tm-signalr-supernetcore-client-2.10.1.tgz
```

Making the tarball:

- `npm pack` in this folder builds the package (`prepack`) and writes `g9tm-signalr-supernetcore-client-2.10.1.tgz`;
  `npm run pack:all` in `js/` packs both workspace packages into `js/artifacts/npm/`.
- `npm run pack:vendor` does the same and moves the `.tgz` into the G9Hub web app's `vendor/` folder
  (`../../../../G9Hub/web/vendor/` from this folder), which is where the G9Hub SPA installs it from.

The tarball holds `dist/` (ESM + `.d.ts`), this README, `PARITY.md` and `LICENSE.md`.

## Connect

```ts
import { G9Client, getG9ErrorCode, G9ErrorCodes } from '@g9tm/signalr-supernetcore-client';

const client = new G9Client({
  url: 'https://hub.example.com/hubs/chat',
  accessTokenFactory: () => session.token,   // called for every connect/reconnect: return a fresh token
  // defaults: statefulReconnect true, keepAliveIntervalMs 15000, serverTimeoutMs 30000, transport 'auto',
  //           logLevel 'warning', reconnect 0/1/2/5/10/20 s then every 30 s (±20% jitter), never gives up
});

client.onState((s) => banner.show(s.phase, s.attempt, s.nextRetryInMs, s.error));
client.onReconnected(({ restarted }) => resync({ full: restarted }));
const off = client.on('ReceiveMessage', (from: string, text: string) => chat.add(from, text));

await client.start();          // resolves once connected; keeps retrying while the server is unreachable

try {
  await client.invoke('SendMessage', 'room-1', 'hello');
} catch (error) {
  if (getG9ErrorCode(error) === G9ErrorCodes.RateLimited) toast('Slow down');
}

await client.waitUntilConnected(10_000);   // after a network change, before sending (TimeoutError otherwise)
off();
await client.stop();
```

State phases: `disconnected`, `connecting` (initial connect loop), `connected`, `reconnecting` (SignalR automatic
reconnect, or a restart after the connection closed for good). `attempt` counts the failed attempts of the current
cycle, `nextRetryInMs` is set while waiting for the next one.

`onReconnected` fires after SignalR's automatic reconnect (`restarted: false`) and after a full restart
(`restarted: true`). SignalR JS resumes a *stateful* connection silently (no event), so whenever `onReconnected`
fires the server sees a new connection: re-join groups and resync; `restarted: true` means the outage was longer
(messages may have been lost).

`start(signal)` rejects when the policy gives up, when `stop()` is called or when `signal` aborts (`AbortError`).

MessagePack: `new G9Client({ url, protocol: new MessagePackHubProtocol() })` — the file transfer classes detect it and
send raw bytes instead of base64.

Runtimes without a global WebSocket or `URL` (Lynx): pass `webSocket` (a WebSocket constructor), and
`transport: 'websockets', skipNegotiation: true` to connect without the negotiate request; `eventSource`, `httpClient`
and `httpTimeoutMs` map to the SignalR options of the same purpose. On Lynx, `createLynxClient()` from
`@g9tm/signalr-supernetcore-lynx` sets all of this.

## Authorize (JWT route)

```ts
import { authorize, G9Client, G9ErrorCodes } from '@g9tm/signalr-supernetcore-client';

const auth = await authorize('https://hub.example.com/auth/chat', { userName, password }, { timeoutMs: 15000 });
if (!auth.isAccepted) {
  throw new Error(auth.rejectionReason === G9ErrorCodes.RateLimited ? 'Too many attempts' : auth.rejectionReason ?? 'Refused');
}
const client = new G9Client({ url: 'https://hub.example.com/hubs/chat', accessTokenFactory: () => auth.jwToken! });
```

`authorize` connects to the auth hub, sends `Authorize(data)`, waits for the `AuthorizeResult` callback and
disconnects. A refusal is a result (`isAccepted: false`, `rejectionReason`), not an exception; 2.9 servers answer a
throttled caller with `rejectionReason: "G9_RATE_LIMITED"`.

## Upload

```ts
import { G9FileUploader, G9UploadStatus } from '@g9tm/signalr-supernetcore-client';

const uploader = new G9FileUploader(client, { onRetry: (r) => log('retry', r.reason, r.backoffMs) });
const controller = new AbortController();

const result = await uploader.upload(file /* File or Blob */, {
  signal: controller.signal,
  onProgress: (p) => bar.set(p.phase, p.bytesSent / p.totalBytes, p.bytesAcknowledged, p.bytesPerSecond),
});

if (result.status === G9UploadStatus.Completed) {
  saveAttachment(result.storedFileName);   // 2.9: the name to download it with (may differ from file.name)
} else {
  showError(result.errorCode);             // e.g. G9_UPLOAD_FORBIDDEN, G9_UPLOAD_TOO_LARGE, G9_UPLOAD_HASH_MISMATCH
}
```

- `upload()` takes a `File`/`Blob`, a `G9ByteSource` (`bytesSource(bytes, { name })`, or any `{ size, name?, read(offset,
  length) }` — what Lynx apps pass) or bytes. `uploadFile(path, { fileSystem })` uploads a file on disk exactly like the
  .NET `UploadAsync(filePath)` (file systems: `nodeFileSystem` from `./node`, `lynxFileSystem()` on Lynx).
- The upload id defaults to the SHA-256 of `name|size|lastModified` (for a file on disk: the .NET key
  `lower(fullPath)|length|lastWriteTicks`, so .NET and TypeScript agree), so the same file resumes where it stopped, even
  after an app restart. When a 2.9 server turns on `PerUserNamespace` and its hub passes the caller as the owner,
  each user's partials are kept apart, so two users with the same file never share one.
- The file is hashed in 4 MiB slices first (`phase: 'hashing'`; pass `sha256` if you already know it), then
  `BeginUpload` tells where to resume and the rest is streamed to `UploadChunks` in 64 KiB chunks.
- Memory stays bounded: at most 8 chunks wait in SignalR's send queue (`maxChunksInFlight`).
- Failed or interrupted attempts are retried (`min(30 s, 200 ms × 2^attempt)`, 5 retries); each waits up to
  `reconnectGraceMs` (2 min) for the connection to come back before calling `BeginUpload` again.
- Aborting rejects with an `AbortError`; the server keeps the partial for the next attempt.
- A refusal while streaming (`G9_UPLOAD_FORBIDDEN` from the server's 2.9 `Authorize` hook, `G9_UPLOAD_TOO_LARGE`,
  `G9_UPLOAD_HASH_MISMATCH`) is a `Failed` result. A refusal of `BeginUpload` itself (forbidden, too large, name or
  metadata conflict) rejects `upload()` with the invocation error. `getG9ErrorCode(error)` finds the code in it only
  when the hub rethrows the service's exception as a `HubException`; otherwise SignalR hides the message.
- `result.storedFileName` (2.9) is the name the server committed the file under; it differs from `file.name` when the
  server randomizes names. Download the file by that name.

## Download

```ts
import { G9FileDownloader, G9TransferError } from '@g9tm/signalr-supernetcore-client';

try {
  const blob = await new G9FileDownloader(client).download(result.storedFileName!, {
    onProgress: (received, total) => bar.set(received / total),
    type: 'image/jpeg',
  });
  img.src = URL.createObjectURL(blob);
} catch (error) {
  if (error instanceof G9TransferError) showError(error.code); // G9_DOWNLOAD_NOT_FOUND / _HASH_MISMATCH / _FAILED
}
```

A broken stream is retried and resumes from the bytes already received; the result is verified against the SHA-256
the server announces. `downloadBytes()` returns a `Uint8Array` instead (every runtime, Lynx included; `download()` needs a
`Blob` global).

To disk, the .NET way — a `.partial` file that survives cancellation and app restarts, SHA-256 check, atomic rename,
results instead of exceptions (twin of `DownloadAsync(serverFileName, localTargetPath)`):

```ts
import { nodeFileSystem } from '@g9tm/signalr-supernetcore-client/node';   // Lynx: lynxFileSystem()

const result = await new G9FileDownloader(client).downloadToFile(result.storedFileName!, '/data/photo.jpg', {
  fileSystem: nodeFileSystem,
  onProgress: (p) => bar.set(p.bytesReceived / p.totalBytes),
});
if (result.status === G9UploadStatus.Completed) open(result.localPath!);   // may carry a .yyyyMMddHHmmssfff stamp
```

## Connection quality

```ts
import { G9ConnectionQualityMonitor } from '@g9tm/signalr-supernetcore-client';

const monitor = new G9ConnectionQualityMonitor(client, { intervalMs: 5000 });
monitor.onChange((q) => indicator.set(q.level, q.rttMs));   // 'good' | 'fair' | 'poor' | 'lost'
monitor.start();
// ...
monitor.stop();
```

It invokes the hub's built-in `G9Ping(long)` (2.9 servers): good < 150 ms, fair < 400 ms, poor ≥ 400 ms or one
failed probe, lost after 3 failed probes in a row or whenever the connection is not connected. `onChange` fires on
level changes only; `monitor.current` always holds the latest measurement. The server rate-limits `G9Ping` (2/s, burst
5): keep `intervalMs` ≥ 500.

## Error codes

`G9ErrorCodes` holds every constant of the server's `G9CErrorCodes` with the same names and values
(`RateLimited`, `ConnectionLimit`, `ConnectionRequired`, `RoleRequired`, `ClaimRequired`, `PermissionRequired`,
`UploadTooLarge`, `UploadHashMismatch`, `UploadUnknownId`, `UploadFailed`, `UploadNameConflict`,
`UploadMetadataConflict`, `UploadForbidden`). `getG9ErrorCode(error)` extracts the code from a rejected `invoke`,
a `G9TransferError`, an upload or authorize result, or a string. Download failures use `G9DownloadErrorCodes`.
The server puts a code into a rejected `invoke` only when it throws a `HubException` (every G9 policy attribute
does); other server exceptions reach the client without their message unless the server enables detailed errors.

## Parity rule

**Every change to the public surface of `G9SignalRSuperNetCore.Client` (methods, options, reconnect behaviour, error
codes, file-transfer DTOs) must be made in this package in the same change, and vice versa.**

| .NET (`G9SignalRSuperNetCore.Client`) | TypeScript (`@g9tm/signalr-supernetcore-client`) |
| --- | --- |
| `G9CClientReconnectPolicy` | `G9ReconnectPolicy` |
| `G9SignalRSuperNetCoreClient` | `G9Client` |
| `G9SignalRSuperNetCoreClientWithJWTAuth` | `authorize()` (+ `G9Client` with `accessTokenFactory`) |
| `G9CFileUploader` | `G9FileUploader` |
| `G9CFileDownloader` | `G9FileDownloader` |
| `G9CConnectionQualityMonitor` | `G9ConnectionQualityMonitor` |
| `G9CErrorCodes` (server) | `G9ErrorCodes` |
| `G9HubConnectionExtensions.WaitUntilConnectedAsync` | `waitUntilConnected()` / `G9Client.waitUntilConnected()` |

The member-by-member mapping, including what has no twin and why and the deliberate behavioural differences, is in
[PARITY.md](./PARITY.md). `test/parity.test.ts` checks the error codes, the file-transfer and authorize DTO property
names, the upload status values and the quality-monitor thresholds against the .NET sources in the repository (in CI a
missing source folder fails the run instead of skipping it).

## Development

```bash
# in js/ (the workspace)
npm ci
npm run verify       # versions, typecheck, tests, Lynx-sandbox tests, build, Lynx runtime gate

# in this folder
npm test             # vitest (unit tests with a HubConnection double and the real JSON / MessagePack protocols)
npm run test:lynxsim # test/lynxsim/scenario.ts bundled and run in a Lynx-shaped sandbox (../../scripts/lynx-sandbox.mjs)
npm run typecheck    # src (ES2019 library) + tests
npm run build        # tsc → dist/ (ES2019 ESM + .d.ts)
npm run pack:vendor  # build + npm pack + copy to the G9Hub web app's vendor/
```

The package version follows the .NET release (2.10.1): `npm run check:versions` in `js/` lists every place that must
equal `G9PackageVersion` in `Directory.Build.props`.

## License

MIT, Copyright (c) 2024-present Iman Kari (G9TM). See [LICENSE.md](./LICENSE.md). Source:
[github.com/ImanKari/G9SignalRSuperNetCore](https://github.com/ImanKari/G9SignalRSuperNetCore) (folder `js/packages/client`).
