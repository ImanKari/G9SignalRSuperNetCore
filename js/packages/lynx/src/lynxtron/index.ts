/// <reference types="node" />
// `@g9tm/signalr-supernetcore-lynx/lynxtron`: the desktop (Lynxtron) side of the native module contract
// (types/g9-signalr-lynx-module.d.ts), for the app's preload, which runs in Node (Lynxtron 0.0.28 embeds Node 22.18):
//
//   import { contextBridge } from '@lynx-js/lynxtron/context-bridge';
//   import { createG9SignalRBridge } from '@g9tm/signalr-supernetcore-lynx/lynxtron';
//   contextBridge.exposeInLynxBTS({ g9signalr: createG9SignalRBridge(), /* other APIs, e.g. G9LynxControls' */ });
//
// The Lynx side then finds it as `NativeModules.nodejs.exposed.g9signalr`. Every method returns a promise of the same
// envelope the Android/iOS module replies with. Tests use it as the reference implementation of the contract.
import { nodeFileSystem } from '@g9tm/signalr-supernetcore-client/node';

/** The envelope every method resolves with. */
export type G9BridgeEnvelope = { ok: true; value: unknown } | { ok: false; error: { code: string; message: string } };

/** A WHATWG-shaped WebSocket constructor (Node's global one, or the `ws` package's). */
export type G9WebSocketConstructor = new (
  url: string,
  init?: string[] | { protocols?: string[]; headers?: Record<string, string> },
) => {
  binaryType: string;
  protocol: string;
  readyState: number;
  onopen: ((event: unknown) => void) | null;
  onmessage: ((event: { data: unknown }) => void) | null;
  onerror: ((event: unknown) => void) | null;
  onclose: ((event: { code?: number; reason?: string; wasClean?: boolean }) => void) | null;
  send(data: string | ArrayBuffer | Uint8Array): void;
  close(code?: number, reason?: string): void;
};

/** Options of {@link createG9SignalRBridge}. */
export interface G9SignalRBridgeOptions {
  /** The WebSocket implementation; default Node's global `WebSocket` (Node 22.4+). */
  WebSocket?: G9WebSocketConstructor;
  /** How the `wsOpen` headers reach the implementation: `init` (`{ protocols, headers }`, Node/undici) or `ws` style. */
  headersStyle?: 'init' | 'ws';
}

type SocketEvent = Record<string, unknown>;

interface SocketState {
  socket: InstanceType<G9WebSocketConstructor>;
  queue: SocketEvent[];
  waiter: ((events: SocketEvent[]) => void) | null;
  closed: boolean;
}

const ok = (value: unknown = null): G9BridgeEnvelope => ({ ok: true, value });
const fail = (code: string, message: string): G9BridgeEnvelope => ({ ok: false, error: { code, message } });

function errorCode(error: unknown): string {
  const code = (error as { code?: unknown })?.code;
  if (code === 'ENOENT') return 'not-found';
  if (code === 'EEXIST') return 'exists';
  return 'io';
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

function toBuffer(data: unknown): ArrayBuffer {
  if (data instanceof ArrayBuffer) return data;
  if (ArrayBuffer.isView(data)) {
    const copy = new Uint8Array(data.byteLength);
    copy.set(new Uint8Array(data.buffer, data.byteOffset, data.byteLength));
    return copy.buffer;
  }
  if (Object.prototype.toString.call(data) === '[object ArrayBuffer]') {
    const copy = new Uint8Array((data as ArrayBuffer).byteLength);
    copy.set(new Uint8Array(data as ArrayBuffer));
    return copy.buffer;
  }
  throw new TypeError('Binary data must be an ArrayBuffer or a typed array.');
}

/** The `g9signalr` object a Lynxtron preload exposes (see the module comment). */
export function createG9SignalRBridge(options: G9SignalRBridgeOptions = {}): Record<string, (...args: never[]) => Promise<G9BridgeEnvelope>> {
  const Impl = options.WebSocket ?? ((globalThis as { WebSocket?: G9WebSocketConstructor }).WebSocket as G9WebSocketConstructor);
  const sockets = new Map<string, SocketState>();

  const push = (state: SocketState, event: SocketEvent): void => {
    state.queue.push(event);
    const waiter = state.waiter;
    if (waiter) {
      state.waiter = null;
      const events = state.queue;
      state.queue = [];
      waiter(events);
    }
  };

  return {
    async capabilities() {
      return ok({ webSocket: typeof Impl === 'function', binary: true, files: true, platform: 'lynxtron', version: '2.10.0' });
    },

    async wsOpen(socketId: string, url: string, protocols: string[], headers: Record<string, string>) {
      if (typeof Impl !== 'function') return fail('unsupported', 'This Node has no WebSocket; pass one to createG9SignalRBridge().');
      if (sockets.has(socketId)) return fail('exists', `Socket ${socketId} is already open.`);
      let socket: InstanceType<G9WebSocketConstructor>;
      try {
        const list = Array.isArray(protocols) ? protocols.slice() : [];
        const hasHeaders = headers && Object.keys(headers).length > 0;
        socket =
          options.headersStyle === 'ws'
            ? new (Impl as unknown as new (u: string, p: string[], o: object) => InstanceType<G9WebSocketConstructor>)(url, list, {
                headers: hasHeaders ? headers : undefined,
              })
            : hasHeaders
              ? new Impl(url, { protocols: list, headers })
              : new Impl(url, list);
      } catch (error) {
        return fail('socket', messageOf(error));
      }
      socket.binaryType = 'arraybuffer';
      const state: SocketState = { socket, queue: [], waiter: null, closed: false };
      sockets.set(socketId, state);
      socket.onopen = () => push(state, { type: 'open', protocol: socket.protocol || '' });
      socket.onmessage = (event) => {
        const data = event.data;
        if (typeof data === 'string') push(state, { type: 'text', data });
        else push(state, { type: 'binary', data: toBuffer(data) });
      };
      socket.onerror = (event) => {
        const message = (event as { message?: unknown; error?: { message?: unknown } })?.message ?? (event as { error?: { message?: unknown } })?.error?.message;
        push(state, { type: 'error', message: typeof message === 'string' ? message : 'WebSocket error.' });
      };
      socket.onclose = (event) => {
        if (state.closed) return;
        state.closed = true;
        push(state, { type: 'close', code: event?.code ?? 1006, reason: event?.reason ?? '', wasClean: event?.wasClean === true });
      };
      return ok();
    },

    async wsSendText(socketId: string, text: string) {
      const state = sockets.get(socketId);
      if (!state || state.closed) return fail('closed', `Socket ${socketId} is not open.`);
      try {
        state.socket.send(String(text));
        return ok();
      } catch (error) {
        return fail('socket', messageOf(error));
      }
    },

    async wsSendBinary(socketId: string, data: ArrayBuffer) {
      const state = sockets.get(socketId);
      if (!state || state.closed) return fail('closed', `Socket ${socketId} is not open.`);
      try {
        state.socket.send(new Uint8Array(toBuffer(data)));
        return ok();
      } catch (error) {
        return fail('socket', messageOf(error));
      }
    },

    async wsClose(socketId: string, code: number, reason: string) {
      const state = sockets.get(socketId);
      if (!state) return ok();
      try {
        if (!state.closed) state.socket.close(code || 1000, reason || '');
      } catch (error) {
        return fail('socket', messageOf(error));
      }
      return ok();
    },

    async wsPoll(socketId: string) {
      const state = sockets.get(socketId);
      if (!state) return ok({ events: [{ type: 'close', code: 1006, reason: 'Unknown socket.', wasClean: false }] });
      if (state.waiter) return fail('busy', 'Only one wsPoll at a time per socket.');
      const events =
        state.queue.length > 0
          ? state.queue.splice(0, state.queue.length)
          : await new Promise<SocketEvent[]>((resolve) => {
              state.waiter = resolve;
            });
      if (events.some((event) => event.type === 'close')) sockets.delete(socketId);
      return ok({ events });
    },

    async fileStat(path: string) {
      try {
        const stat = await nodeFileSystem.stat(path);
        return ok(stat ? { exists: true, size: stat.size, fullPath: stat.fullPath, lastWriteTicks: stat.lastWriteTicks } : { exists: false });
      } catch (error) {
        return fail(errorCode(error), messageOf(error));
      }
    },

    async fileRead(path: string, offset: number, length: number) {
      try {
        const bytes = await nodeFileSystem.read(path, offset, length);
        return ok({ data: toBuffer(bytes) });
      } catch (error) {
        return fail(errorCode(error), messageOf(error));
      }
    },

    async fileWrite(path: string, offset: number, data: ArrayBuffer, truncate: boolean) {
      try {
        await nodeFileSystem.write(path, offset, new Uint8Array(toBuffer(data)), truncate === true);
        return ok();
      } catch (error) {
        return fail(errorCode(error), messageOf(error));
      }
    },

    async fileMove(from: string, to: string) {
      try {
        await nodeFileSystem.move(from, to);
        return ok();
      } catch (error) {
        return fail(errorCode(error), messageOf(error));
      }
    },

    async fileDelete(path: string) {
      try {
        await nodeFileSystem.remove(path);
        return ok();
      } catch (error) {
        return fail(errorCode(error), messageOf(error));
      }
    },
  };
}

/** The one Lynxtron API this needs (`@lynx-js/lynxtron/context-bridge`); passed in so this package does not depend on it. */
export interface G9ContextBridge {
  exposeInLynxBTS(apis: Record<string, unknown>): void;
}

/**
 * Exposes `g9signalr` (plus `extra`, e.g. other libraries' bridges) in ONE `exposeInLynxBTS` call — prefer a single
 * call with every API of the app over several calls.
 */
export function exposeG9SignalR(contextBridge: G9ContextBridge, extra: Record<string, unknown> = {}, options?: G9SignalRBridgeOptions): void {
  contextBridge.exposeInLynxBTS({ ...extra, g9signalr: createG9SignalRBridge(options) });
}
