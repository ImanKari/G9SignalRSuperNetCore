# @g9tm/signalr-supernetcore-lynx

The [Lynx](https://lynxjs.org) platform layer of
[`@g9tm/signalr-supernetcore-client`](../client/README.md) (release **2.10.1**, the version of the G9SignalRSuperNetCore
NuGet packages). The client itself is runtime-neutral; Lynx's background thread lacks what a SignalR client needs, and
this package supplies it:

| Lynx lacks | This package | Platforms |
| --- | --- | --- |
| a WebSocket (release builds have none; the devtool's `LynxWebSocketModule` is debug-only and text-only) | `G9LynxWebSocket` over the native module — **text and binary** frames, so MessagePack works | Android (OkHttp 4.12), iOS (`NSURLSessionWebSocketTask`), Windows/macOS (Lynxtron: Node's WebSocket), Web (the browser's) |
| file access | `lynxFileSystem()` — `G9FileUploader.uploadFile` / `G9FileDownloader.downloadToFile` exactly like .NET (same upload id, `.partial` resume) | Android, iOS, Lynxtron |
| `URL` / `URLSearchParams` (SignalR's negotiate step needs them) | `installUrlShim()` (installed for you when negotiation is used) | all |
| sensible defaults | `createLynxClient()`, `lynxAuthorize()`, `lynxClientOptions()` | all |

## Install

```bash
npm install @g9tm/signalr-supernetcore-lynx@2.10.1 @g9tm/signalr-supernetcore-client@2.10.1 @microsoft/signalr@^10
npm install @microsoft/signalr-protocol-msgpack@^10    # optional, binary frames (recommended on mobile)
```

### Host setup

The package is a Lynx native library (`lynx.lib.json`), like G9LynxControls' `@g9lynx/native`.

- **Android** — Lynx Autolink links `android/` from `node_modules` (`com.g9tm.signalrlynx`, module
  `G9SignalRLynxModule`); `lynx-processor` generates the provider that registers it. The library brings OkHttp 4.12
  (an app on OkHttp 5 resolves to that) and the `INTERNET` permission. minSdk 26, compileSdk 36, Lynx 4.1.0.
- **iOS** — CocoaPods Autolink adds the `g9tm-signalr-lynx` pod (iOS 15, Lynx 4.0.3). Lynx 4.0.3 has no
  self-registration: register the module where you build the `LynxConfig`:
  ```objc
  #import "G9SignalRLynxModule.h"
  [config registerModule:G9SignalRLynxModule.class];
  ```
- **Windows / macOS (Lynxtron)** — expose the bridge from the preload, in the same `exposeInLynxBTS` call as the app's
  other APIs (e.g. G9LynxControls' `g9sqlite`):
  ```ts
  import { contextBridge } from '@lynx-js/lynxtron/context-bridge';
  import { createG9SignalRBridge } from '@g9tm/signalr-supernetcore-lynx/lynxtron';
  contextBridge.exposeInLynxBTS({ g9signalr: createG9SignalRBridge() /*, g9sqlite: … */ });
  ```
  The Lynx side finds it as `NativeModules.nodejs.exposed.g9signalr`. Node 22.4+ (Lynxtron 0.0.28 embeds 22.18) has a
  global WebSocket; pass `{ WebSocket }` from `ws` to `createG9SignalRBridge` for an older Node.
- **Lynx for Web** — nothing: the browser's WebSocket is used.

## Connect

```ts
import { createLynxClient, lynxAuthorize } from '@g9tm/signalr-supernetcore-lynx';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';

const auth = await lynxAuthorize('https://host/AuthHub', credentials);
const client = createLynxClient({
  url: 'https://host/SecureHub',
  protocol: new MessagePackHubProtocol(),
  accessTokenFactory: () => auth.jwToken!,
  onTransportChosen: (choice) => log('transport', choice),
});
client.onState((s) => banner.show(s.phase));
await client.start();
```

`createLynxClient` takes every `G9ClientOptions` option (reconnect policy, stateful reconnect, headers, …) and chooses
the transport:

| Situation | Choice | What happens |
| --- | --- | --- |
| the native module is there (Android, iOS, Lynxtron) | `native-websocket` | `G9LynxWebSocket`, `transport: 'websockets'`, `skipNegotiation: true` — straight to the hub, one round trip less, no `URL` needed. The access token goes in `?access_token=` (configure the hub's JWT bearer events to read it, as for browsers); `headers` also reach the upgrade request |
| the same, with `skipNegotiation: false` | `native-websocket-negotiated` | negotiate over Lynx's `fetch` first (needed behind Azure SignalR Service or for stateful reconnect), URL shim installed |
| no native module, a platform WebSocket (Lynx for Web) | `platform` | SignalR's normal negotiation |
| neither | `long-polling` | long polling over `fetch` (JSON or MessagePack). Works everywhere Lynx has its HTTP service, but every server message costs a poll: link the native module for real use |

SignalR builds an HTTP client even when it never sends a request; without `fetch` (a host without Lynx's HTTP service)
`createLynxClient` supplies one that refuses, so a native-WebSocket connection still works.

## Files

```ts
import { G9FileDownloader, G9FileUploader, G9UploadStatus } from '@g9tm/signalr-supernetcore-client';
import { lynxFileSystem } from '@g9tm/signalr-supernetcore-lynx';

const fileSystem = lynxFileSystem();
const up = await new G9FileUploader(client).uploadFile('/data/user/0/app/files/photo.jpg', { fileSystem });
const down = await new G9FileDownloader(client).downloadToFile(up.storedFileName!, `${dir}/photo.jpg`, { fileSystem });
if (down.status === G9UploadStatus.Completed) show(down.localPath!);
```

Paths are the platform's absolute paths. `stat` reports the .NET `FileInfo` facts, so `uploadFile` computes the same
upload id as the .NET `G9CFileUploader` for the same file, and an interrupted upload resumes after an app restart.
`lynxFileSource(path)` gives a `G9ByteSource` for `upload()` when you want your own upload id or name.

## The native module contract

`types/g9-signalr-lynx-module.d.ts` is the contract (`npm run codegen` generates the Android/iOS specs and
`generated/`). Every reply is an envelope `{ ok, value | error: { code, message } }`. Sockets deliver events through
`wsPoll` (one outstanding poll per socket, events batched), so the same JavaScript adapter works over Lynx's callback
bridge and Lynxtron's promise bridge. `getG9SignalRNative()` returns the detected module; `setG9SignalRNative()` replaces
it (tests, or an app that implements the contract itself).

## Development

```bash
npm test                      # unit + end-to-end over a real WebSocket (the contract's Node implementation)
npm run test:lynxsim          # the package bundled and run in a Lynx-shaped sandbox (no WebSocket/URL/TextEncoder/BigInt)
npm run test:interop          # the sandbox against the real .NET sample server: JSON + MessagePack × every transport path
npm run test:android-jvm      # Kotlin core (OkHttp socket, files) on the JVM
npm run check:android-module  # the module compiled against the Lynx 4.1.0 classes; Autolink provider generated
npm run test:ios-core         # iOS file core: Apple clang on macOS, GNUstep on Linux / WSL
```

What only a device can prove is listed in [Native-Validation-Pending.md](./Native-Validation-Pending.md).

## License

MIT, Copyright (c) 2024-present Iman Kari (G9TM). See [LICENSE.md](./LICENSE.md). Source:
[github.com/ImanKari/G9SignalRSuperNetCore](https://github.com/ImanKari/G9SignalRSuperNetCore) (folder `js/packages/lynx`).
