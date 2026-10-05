# Native validation — what is proven, what is pending

The ledger of `@g9tm/signalr-supernetcore-lynx`'s native code. A row moves to **proven** only with the command that
proved it and the date. Nothing here is claimed from a Node test.

## Proven (2026-10-05, Windows 11, Node 24.12, JDK 21 (Android Studio jbr), Gradle 8.14.3, WSL Ubuntu 24.04)

| What | How | Result |
| --- | --- | --- |
| JS adapter ↔ contract, both bridge shapes (Android/iOS callbacks, Lynxtron promises) | `npm test` (`test/webSocket.test.ts`, `test/client.test.ts`) over a real WebSocket server | text + binary in order, sub-protocol, upgrade headers, server close codes, failed connects, files |
| The whole package on a Lynx-shaped runtime | `npm run test:lynxsim` | no WebSocket/URL/TextEncoder/BigInt in the sandbox; JSON + MessagePack, streams, pushes, files |
| Interop with the real ASP.NET Core server | `npm run test:interop` (G9SignalRSuperNetCore.WebServer) | 6/6: {JSON, MessagePack} × {native WS, native WS negotiated, long polling} |
| Android socket + file core (`android/…/core`) | `npm run test:android-jvm` — OkHttp MockWebServer | 8/8 |
| Android module vs the Lynx 4.1.0 classes, Autolink provider | `npm run check:android-module` (kapt + lynx-processor) | compiles; `LynxLibraryProviderImpl` registers `G9SignalRLynxModule` |
| Every `@LynxMethod` signature is one Lynx's Android bridge accepts | `npm run check:android-module` → `LynxBridgeSignatureTest` (Lynx 4.1.0's own `LynxMethodWrapper`); `npm test` → `scripts/android-bridge-types.mjs` | all 11 methods, e.g. `wsOpen` `v.TTAMX`, `fileWrite` `v.TdazX` (before the fix every method failed with `Got unknown param class: Object`: codegen 0.6.0 writes `Object` for functions, objects, arrays and ArrayBuffers, and one such parameter leaves the module without methods in JS) |
| **N-SR-01 (core)** The WebSocket half of the Android module inside a real Lynx host, the package AS PACKED: Autolink, Lynx's callback bridge, `wsOpen` with protocols/headers, binary frames both ways (`byte[]` in, `putByteArray` → ArrayBuffer out of `wsPoll`), text frames, events from OkHttp's threads | G9SyncData's `js/apps/lynx-android-host`: `node scripts/device-test.mjs --install --protocol both` — emulator-5554 (AVD Pixel 9 Pro XL, Android 16 / API 36, x86_64), Lynx 4.1.0, PrimJS 4.1.1, 2026-10-05 | a whole G9SyncData sync over it PASSES on both hub protocols: MessagePack (49 `wsSendBinary`), JSON (51 `wsSendText`), with pushes, a server poke and streams |
| Lynxtron bridge on Node 22 (what Lynxtron 0.0.28 embeds): a refused connect ends with `error` then `close` 1006 | `npm test` under Node 22.23.3 (`test/webSocket.test.ts`; Node 22's WebSocket — undici 6 — reports a refused connect with `error` and never `close`, readyState stuck at CONNECTING: the bridge now closes it itself; found by the hosted Windows agent, 2026-10-06) | 9/9 on Node 22 and Node 24 |
| iOS file core (`ios/src/core/G9SignalRFileCore.m`) | `npm run test:ios-core` (clang + GNUstep in WSL) | 14/14 checks |

## Pending (needs a device, an emulator or a Mac)

| Id | What | Why it is not proven yet | How to prove it |
| --- | --- | --- | --- |
| N-SR-01 | The rest of the Android module in a real host (the WebSocket core is proven above): the file operations (`fileStat/Read/Write/Move/Delete` behind the transfers), server-initiated close codes, and a minified (R8) release build | the device run is a G9SyncData sync in a Debug APK: no file transfer, no R8 | G9SyncData's `js/apps/lynx-android-host` pattern: a page that runs an upload and a download against the sample WebServer, then `assembleRelease` with minify on |
| N-SR-02 | iOS socket core (`G9SignalRSocketCore.m`, `NSURLSessionWebSocketTask`) and the module class | no macOS/Xcode here; GNUstep has no NSURLSession WebSocket | Xcode: an iOS host registering `G9SignalRLynxModule`, the same scenario |
| N-SR-03 | Lynxtron's real `contextBridge` (`exposeInLynxBTS` with `g9signalr`, binary over the bridge) | the Lynxtron runtime is not part of this repository's tests | G9LynxControls' `apps/desktop-host` pattern: a headless Lynxtron run of a test bundle |
| N-SR-04 | Throughput on a weak device (bridge cost per message, MessagePack chunk sizes) | performance is a device property | measure a 50 MB download and a 500k-row G9SyncData pull on the DOOGEE S99 |
