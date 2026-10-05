// The native module of @g9tm/signalr-supernetcore-lynx. `npm run codegen` turns this declaration into the Android/iOS
// specs (android/…/generated, ios/src/generated) and the JS accessor (generated/). Every reply is an envelope
// `{ ok: true, value } | { ok: false, error: { code, message } }` so error fields survive the bridge.

/** @lynxmodule */
export declare class G9SignalRLynxModule {
  /** `{ webSocket: true, binary: true, files: true, platform, version }`. */
  capabilities(callback: (reply: Record<string, unknown>) => void): void;
  /** Starts connecting; the outcome (and everything after it) arrives through `wsPoll`. */
  wsOpen(
    socketId: string,
    url: string,
    protocols: string[],
    headers: Record<string, unknown>,
    callback: (reply: Record<string, unknown>) => void,
  ): void;
  wsSendText(socketId: string, text: string, callback: (reply: Record<string, unknown>) => void): void;
  wsSendBinary(socketId: string, data: ArrayBuffer, callback: (reply: Record<string, unknown>) => void): void;
  wsClose(socketId: string, code: number, reason: string, callback: (reply: Record<string, unknown>) => void): void;
  /**
   * Replies with every event queued for the socket (`{ events: [...] }`), waiting until there is at least one. One poll
   * at a time per socket. Event shapes: `{ type: 'open', protocol }`, `{ type: 'text', data }`,
   * `{ type: 'binary', data: ArrayBuffer }`, `{ type: 'error', message }`, `{ type: 'close', code, reason, wasClean }`
   * (always the last one).
   */
  wsPoll(socketId: string, callback: (reply: Record<string, unknown>) => void): void;
  /** `{ exists: false }` or `{ exists: true, size, fullPath, lastWriteTicks }` (ticks: decimal string). */
  fileStat(path: string, callback: (reply: Record<string, unknown>) => void): void;
  /** `{ data: ArrayBuffer }`: `length` bytes from `offset`, fewer only at the end of the file. */
  fileRead(path: string, offset: number, length: number, callback: (reply: Record<string, unknown>) => void): void;
  /** Writes at `offset` (creating the file and its directories); `truncate` cuts the file after the written bytes. */
  fileWrite(
    path: string,
    offset: number,
    data: ArrayBuffer,
    truncate: boolean,
    callback: (reply: Record<string, unknown>) => void,
  ): void;
  /** Renames; fails with `exists` when the target exists. */
  fileMove(from: string, to: string, callback: (reply: Record<string, unknown>) => void): void;
  /** Deletes; no error when the file does not exist. */
  fileDelete(path: string, callback: (reply: Record<string, unknown>) => void): void;
}
