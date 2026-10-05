# Agent instructions — G9SignalRSuperNetCore

G9SignalRSuperNetCore is a redistributable library family: four NuGet packages (.NET) and two npm packages
(TypeScript, browser/Node/**Lynx**), released together under ONE version. Treat every public type, hub method name,
DTO member name, error code and native-module method as public API.

| Side | Where | Packages |
|---|---|---|
| .NET | `G9SignalRSuperNetCore/` (solution) | `G9SignalRSuperNetCore.Server`, `.Client`, `.Server.MessagePack`, `.Client.MessagePack` |
| TypeScript | `js/packages/client` | `@g9tm/signalr-supernetcore-client` — twin of `.Client` (+ `./node`) |
| Lynx | `js/packages/lynx` | `@g9tm/signalr-supernetcore-lynx` — native module (Android Kotlin, iOS Objective-C, Lynxtron/Node) + Lynx defaults |

## The rule that overrides convenience: one change, every side

A task that changes the client contract is done only when **every side has it**, in the same change:

1. **.NET client surface** (methods, options, reconnect behaviour, error codes, file-transfer DTOs, hub method names the
   client calls) → the same change in `js/packages/client` under the TypeScript naming conventions, a row in
   `js/packages/client/PARITY.md`, and a test on both sides. And the other way round.
2. **Server contract** both clients rely on (hub method names, DTO member names — they ARE the wire format — `G9_*`
   codes, `G9Ping`, `Authorize`/`AuthorizeResult`) → both clients, `parity.test.ts` still green.
3. **Native module contract** (`js/packages/lynx/types/g9-signalr-lynx-module.d.ts`) → `npm run codegen -w
   packages/lynx`, then Android (`android/…/G9SignalRLynxModule.kt` + `core/`), iOS (`ios/src/…`), the Lynxtron bridge
   (`src/lynxtron/index.ts`) and the JS adapter (`src/native.ts`) together; a JVM test for the Kotlin core, a GNUstep
   check for the iOS core where it can run, and a row in `js/packages/lynx/Native-Validation-Pending.md` for what only a
   device proves. **Android parameter types:** Lynx's bridge accepts only `Callback`, `ReadableMap`, `ReadableArray`,
   `byte[]`, `String`, primitives (and `Promise`/`Dynamic`); ONE `Object`/`Any?` parameter leaves the whole module
   without methods in JS. `@lynx-js/autolink-codegen` 0.6.0 writes `Object` for functions, objects, arrays and
   ArrayBuffers, so `npm run codegen` runs `scripts/android-bridge-types.mjs --fix` after it; the Kotlin overrides take
   `Callback?`, `ReadableMap?`, `ReadableArray?`, `ByteArray?`. `npm test` checks spec + module against the declaration,
   and `check:android-module` builds every signature with Lynx's own `LynxMethodWrapper` (`LynxBridgeSignatureTest`).
   The same script lives in G9SyncData's `js/packages/lynx/scripts` and G9LynxControls'
   `packages/native/scripts` — keep the three copies identical.
4. **Version**: `<G9PackageVersion>` in `G9SignalRSuperNetCore/Directory.Build.props` is the single coordinate.
   `npm run check:versions` (in `js/`) lists every other place that must equal it.
5. **Docs** move with the code: root `README.md` (What's new + Migration), the package READMEs, PARITY.md.

A behaviour that cannot exist on one side (a file path on a browser, a `Blob` in .NET) is a *documented* difference in
PARITY.md, never a silent gap.

## Lynx is not the web (the TypeScript side ships to Lynx's background thread)

- No `BigInt`, `Intl`, `TextEncoder`/`TextDecoder`, `btoa`/`atob`, `URL`, `Blob`, `crypto`, `performance`,
  `queueMicrotask`; timers are bundle-scope identifiers, never `globalThis.setTimeout`. Release builds have **no
  WebSocket** — the native module provides it.
- Shipped code targets ES2019 with the ES2019 library (`js/tsconfig.base.json`); tests may use newer APIs.
- `npm run test:lynxsim` runs the packages in a Lynx-shaped `node:vm` sandbox (`js/scripts/lynx-sandbox.mjs`), and
  `npm run check:lynx` scans sources and `dist/` — both are part of `npm run verify`.
- Background facts and their evidence: G9LynxControls `plan/specs/02-Lynx-Platform-Rules.md`.

## Commands

```powershell
dotnet build G9SignalRSuperNetCore/G9SignalRSuperNetCore.sln -c Release
dotnet test G9SignalRSuperNetCore/G9SignalRSuperNetCore.Tests -c Release
```

```bash
cd js && npm ci && npm run verify          # versions, typecheck, tests, lynxsim, build, Lynx runtime gate
npm run test:interop -w packages/lynx      # Lynx sandbox vs the real sample server (needs the .NET SDK)
npm run test:android-jvm -w packages/lynx  # Kotlin core on the JVM (JDK 17+, Gradle 8.14; scripts/run-gradle.mjs finds them)
npm run check:android-module -w packages/lynx   # compile + LynxBridgeSignatureTest (Lynx's own signature builder)
npm run test:ios-core -w packages/lynx     # GNUstep (Linux / WSL)
# On a real Lynx runtime (Android emulator): G9SyncData's js/apps/lynx-android-host runs a full sync over this module.
```

## Git

Work on `main` (the pipeline publishes from it). Never commit, push, tag or publish unless asked in that turn.
