// @g9tm/signalr-supernetcore-lynx — the Lynx platform layer of @g9tm/signalr-supernetcore-client (2.10.1).
// The client (G9Client, authorize, transfers, quality monitor) is runtime-neutral; this package adds what Lynx lacks:
// a binary WebSocket (native module on Android/iOS, the Lynxtron preload on desktop), file access for the .NET-twin
// file transfers, a URL shim for negotiation, and a factory that picks all of it.
// Parity rule: see ../client/PARITY.md — a change to the .NET client is made in the TypeScript client in the same
// change; a change to the native module contract (types/g9-signalr-lynx-module.d.ts) is made on Android, iOS and the
// Lynxtron bridge in the same change.

/** The package version (kept equal to the .NET library release and to the client package). */
export const VERSION = '2.10.1';

export {
  createLynxClient,
  lynxAuthorize,
  lynxClientOptions,
  type G9LynxClientOptions,
  type G9LynxTransportChoice,
} from './client.js';

export { createLynxWebSocket, G9LynxWebSocket, type G9LynxSocketEvent, type G9LynxWebSocketOptions } from './webSocket.js';

export { lynxFileSource, lynxFileSystem } from './fileSystem.js';

export { G9Url, G9UrlSearchParams, installUrlShim } from './url.js';

export {
  fromBridge,
  G9NativeModuleError,
  getG9SignalRNative,
  nativeFromCallbackModule,
  nativeFromPromiseModule,
  setG9SignalRNative,
  type G9NativeCapabilities,
  type G9NativeError,
  type G9NativeFileStat,
  type G9NativeSocketEvent,
  type G9SignalRNative,
} from './native.js';
