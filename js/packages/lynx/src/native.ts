// The one place that talks to the platform: the `G9SignalRLynxModule` native module (Android/iOS, Autolink) or the
// `g9signalr` bridge a Lynxtron preload exposes (desktop, `NativeModules.nodejs.exposed.g9signalr`). Both speak the
// same envelopes (types/g9-signalr-lynx-module.d.ts); this file turns them into promises and normalizes values that
// crossed a bridge (binary data may arrive as another realm's ArrayBuffer or as a typed array).

/** A native error, as data. */
export interface G9NativeError {
  readonly code: string;
  readonly message: string;
}

/** Every reply of the module. */
export type G9NativeEnvelope<T> =
  | { readonly ok: true; readonly value: T }
  | { readonly ok: false; readonly error: G9NativeError };

/** One event of `wsPoll`. */
export type G9NativeSocketEvent =
  | { readonly type: 'open'; readonly protocol?: string }
  | { readonly type: 'text'; readonly data: string }
  | { readonly type: 'binary'; readonly data: ArrayBuffer }
  | { readonly type: 'error'; readonly message?: string }
  | { readonly type: 'close'; readonly code?: number; readonly reason?: string; readonly wasClean?: boolean };

/** What `capabilities` reports. */
export interface G9NativeCapabilities {
  readonly webSocket: boolean;
  readonly binary: boolean;
  readonly files: boolean;
  readonly platform: string;
  readonly version: string;
}

/** `fileStat` of an existing file. */
export interface G9NativeFileStat {
  readonly exists: true;
  readonly size: number;
  readonly fullPath: string;
  readonly lastWriteTicks: string;
}

/** The promise face of the module, the same for every platform. */
export interface G9SignalRNative {
  readonly kind: 'native-module' | 'lynxtron' | 'custom';
  capabilities(): Promise<G9NativeCapabilities>;
  wsOpen(socketId: string, url: string, protocols: string[], headers: Record<string, string>): Promise<void>;
  wsSendText(socketId: string, text: string): Promise<void>;
  wsSendBinary(socketId: string, data: ArrayBuffer): Promise<void>;
  wsClose(socketId: string, code: number, reason: string): Promise<void>;
  wsPoll(socketId: string): Promise<G9NativeSocketEvent[]>;
  fileStat(path: string): Promise<G9NativeFileStat | null>;
  fileRead(path: string, offset: number, length: number): Promise<ArrayBuffer>;
  fileWrite(path: string, offset: number, data: ArrayBuffer, truncate: boolean): Promise<void>;
  fileMove(from: string, to: string): Promise<void>;
  fileDelete(path: string): Promise<void>;
}

/** Raised for an `{ ok: false }` reply; `code` is the native error code (`exists`, `not-found`, `io`, `socket`, …). */
export class G9NativeModuleError extends Error {
  readonly code: string;

  constructor(error: G9NativeError | undefined) {
    super(error?.message || 'The native module failed.');
    this.name = 'G9NativeModuleError';
    this.code = error?.code || 'unknown';
  }
}

type Callback = (reply: unknown) => void;
type CallbackModule = Record<string, (...args: unknown[]) => void>;
type PromiseModule = Record<string, (...args: unknown[]) => unknown>;

declare const NativeModules:
  | (Record<string, unknown> & { nodejs?: { exposed?: Record<string, unknown> } })
  | undefined;

const tagOf = (value: unknown): string => Object.prototype.toString.call(value);

/** A bridge value with binary data turned into this realm's ArrayBuffer (recursively). */
export function fromBridge(value: unknown): unknown {
  if (value === null || typeof value !== 'object') return value;
  if (value instanceof ArrayBuffer) return value;
  const tag = tagOf(value);
  if (tag === '[object ArrayBuffer]') {
    const copy = new Uint8Array((value as ArrayBuffer).byteLength);
    copy.set(new Uint8Array(value as ArrayBuffer));
    return copy.buffer;
  }
  if (ArrayBuffer.isView(value) || /^\[object (Uint8|Int8|Uint8Clamped)Array\]$/.test(tag)) {
    const view = value as ArrayBufferView;
    const copy = new Uint8Array(view.byteLength);
    copy.set(new Uint8Array(view.buffer, view.byteOffset, view.byteLength));
    return copy.buffer;
  }
  if (Array.isArray(value)) return value.map(fromBridge);
  const out: Record<string, unknown> = {};
  for (const key of Object.keys(value)) out[key] = fromBridge((value as Record<string, unknown>)[key]);
  return out;
}

function unwrap<T>(reply: unknown): T {
  const envelope = fromBridge(reply) as G9NativeEnvelope<T> | undefined;
  if (envelope && envelope.ok === true) return envelope.value;
  throw new G9NativeModuleError(envelope && envelope.ok === false ? envelope.error : undefined);
}

/** An ArrayBuffer holding exactly `bytes` (the bridge sends a whole buffer, never a view's window). */
export function exactBuffer(bytes: ArrayBuffer | ArrayBufferView): ArrayBuffer {
  if (bytes instanceof ArrayBuffer) return bytes;
  const view = new Uint8Array(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  if (view.byteOffset === 0 && view.byteLength === view.buffer.byteLength && view.buffer instanceof ArrayBuffer) {
    return view.buffer;
  }
  return view.slice().buffer as ArrayBuffer;
}

function fromCallbacks(module: CallbackModule): G9SignalRNative {
  const call = <T>(method: string, ...args: unknown[]): Promise<T> =>
    new Promise<T>((resolve, reject) => {
      const fn = module[method];
      if (typeof fn !== 'function') {
        reject(new G9NativeModuleError({ code: 'unsupported', message: `G9SignalRLynxModule.${method} is missing.` }));
        return;
      }
      try {
        fn.call(module, ...args, ((reply: unknown) => {
          try {
            resolve(unwrap<T>(reply));
          } catch (error) {
            reject(error);
          }
        }) as Callback);
      } catch (error) {
        reject(error);
      }
    });
  return build('native-module', call);
}

function fromPromises(bridge: PromiseModule): G9SignalRNative {
  const call = async <T>(method: string, ...args: unknown[]): Promise<T> => {
    const fn = bridge[method];
    if (typeof fn !== 'function') {
      throw new G9NativeModuleError({ code: 'unsupported', message: `g9signalr.${method} is missing.` });
    }
    return unwrap<T>(await fn.call(bridge, ...args));
  };
  return build('lynxtron', call);
}

function build(kind: G9SignalRNative['kind'], call: <T>(method: string, ...args: unknown[]) => Promise<T>): G9SignalRNative {
  return {
    kind,
    capabilities: () => call('capabilities'),
    wsOpen: (id, url, protocols, headers) => call<void>('wsOpen', id, url, protocols, headers),
    wsSendText: (id, text) => call<void>('wsSendText', id, text),
    wsSendBinary: (id, data) => call<void>('wsSendBinary', id, data),
    wsClose: (id, code, reason) => call<void>('wsClose', id, code, reason),
    wsPoll: async (id) => ((await call<{ events?: G9NativeSocketEvent[] }>('wsPoll', id))?.events ?? []),
    fileStat: async (path) => {
      const stat = await call<{ exists?: boolean; size?: number; fullPath?: string; lastWriteTicks?: unknown }>('fileStat', path);
      if (!stat || !stat.exists) return null;
      return {
        exists: true,
        size: Number(stat.size ?? 0),
        fullPath: String(stat.fullPath ?? path),
        lastWriteTicks: String(stat.lastWriteTicks ?? '0'),
      };
    },
    fileRead: async (path, offset, length) => {
      const value = await call<{ data?: ArrayBuffer }>('fileRead', path, offset, length);
      return value?.data ?? new ArrayBuffer(0);
    },
    fileWrite: (path, offset, data, truncate) => call<void>('fileWrite', path, offset, data, truncate),
    fileMove: (from, to) => call<void>('fileMove', from, to),
    fileDelete: (path) => call<void>('fileDelete', path),
  };
}

let override: G9SignalRNative | null | undefined;

/**
 * Replaces the platform module (tests, or an app that implements the module contract itself). `null` forces "no
 * native module"; `undefined` restores detection.
 */
export function setG9SignalRNative(native: G9SignalRNative | null | undefined): void {
  override = native;
}

/** The platform module: Android/iOS native module, the Lynxtron preload bridge, or `null` when neither exists. */
export function getG9SignalRNative(): G9SignalRNative | null {
  if (override !== undefined) return override;
  let modules: typeof NativeModules;
  try {
    modules = typeof NativeModules === 'undefined' ? undefined : NativeModules;
  } catch {
    modules = undefined;
  }
  if (!modules) return null;
  const native = modules.G9SignalRLynxModule as CallbackModule | undefined;
  if (native && typeof native === 'object') return fromCallbacks(native);
  const desktop = modules.nodejs?.exposed?.g9signalr as PromiseModule | undefined;
  if (desktop && typeof desktop === 'object') return fromPromises(desktop);
  return null;
}

/** Wraps an object with the module's callback methods (the Android/iOS shape) — for apps and tests. */
export function nativeFromCallbackModule(module: object): G9SignalRNative {
  return fromCallbacks(module as CallbackModule);
}

/** Wraps an object with the bridge's promise methods (the Lynxtron shape) — for apps and tests. */
export function nativeFromPromiseModule(bridge: object): G9SignalRNative {
  return fromPromises(bridge as PromiseModule);
}
