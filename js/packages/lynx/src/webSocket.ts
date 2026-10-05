// A browser-shaped WebSocket over the G9SignalRLynxModule, for SignalR's WebSockets transport on Lynx (whose release
// builds have no WebSocket; the devtool's LynxWebSocketModule is text-only and absent from release builds). Text AND
// binary frames, so the MessagePack hub protocol works.
//
// The native side queues socket events; this class keeps exactly one `wsPoll` outstanding and dispatches what each poll
// returns, in order. Sends are forwarded in call order; a failed send closes the socket (1006), as a browser would.
import { exactBuffer, getG9SignalRNative, type G9NativeSocketEvent, type G9SignalRNative } from './native.js';

/** What SignalR and other WebSocket users read from an event. */
export interface G9LynxSocketEvent {
  readonly type: string;
  readonly target: G9LynxWebSocket;
  readonly data?: string | ArrayBuffer;
  readonly code?: number;
  readonly reason?: string;
  readonly wasClean?: boolean;
  readonly message?: string;
}

type Listener = (event: G9LynxSocketEvent) => void;

/** Options of {@link createLynxWebSocket}. */
export interface G9LynxWebSocketOptions {
  /**
   * Headers sent with the WebSocket upgrade request (a browser cannot; the native socket can). SignalR passes the
   * access token in the query string for WebSockets either way.
   */
  headers?: Record<string, string>;
  /** The platform module; default the detected one (`getG9SignalRNative()`). */
  native?: G9SignalRNative;
}

let nextId = 1;

/** Socket ids are unique per JS context; a random prefix keeps two LynxViews of one app apart in the shared module. */
const idPrefix = (() => {
  let text = '';
  for (let i = 0; i < 8; i++) text += Math.floor(Math.random() * 36).toString(36);
  return text;
})();

/**
 * The WebSocket class SignalR is given (`webSocket` option). Browser surface: `readyState`, `binaryType`
 * (`'arraybuffer'`; `'blob'` is refused), `protocol`, `url`, `bufferedAmount` (always 0), `send`, `close`, the `on*`
 * handlers and `addEventListener`/`removeEventListener`, and the `CONNECTING`/`OPEN`/`CLOSING`/`CLOSED` constants.
 */
export class G9LynxWebSocket {
  static readonly CONNECTING = 0;
  static readonly OPEN = 1;
  static readonly CLOSING = 2;
  static readonly CLOSED = 3;
  readonly CONNECTING = 0;
  readonly OPEN = 1;
  readonly CLOSING = 2;
  readonly CLOSED = 3;

  readonly url: string;
  readonly extensions = '';
  readonly bufferedAmount = 0;
  protocol = '';
  readyState = 0;
  onopen: Listener | null = null;
  onmessage: Listener | null = null;
  onerror: Listener | null = null;
  onclose: Listener | null = null;

  private _binaryType: 'arraybuffer' = 'arraybuffer';
  private readonly _id: string;
  private readonly _native: G9SignalRNative;
  private readonly _listeners = new Map<string, Set<Listener>>();
  private _sending: Promise<void> = Promise.resolve();
  private _closeFired = false;

  /** Headers of every socket this class creates (see {@link createLynxWebSocket}). */
  protected static defaultHeaders: Record<string, string> = {};
  /** The module every socket this class creates uses; detected when unset. */
  protected static defaultNative: G9SignalRNative | undefined;

  /**
   * @param url `ws(s)://` URL (SignalR passes the hub URL with `http` replaced).
   * @param protocols Sub-protocols.
   * @param options `{ headers }` for the upgrade request — the `ws`-package signature SignalR uses on Node/React
   *   Native (there it sends the token as `Authorization` instead of `?access_token=`). Merged over the class headers.
   */
  constructor(url: string, protocols?: string | string[], options?: { headers?: Record<string, string> }) {
    const ctor = this.constructor as typeof G9LynxWebSocket;
    const native = ctor.defaultNative ?? getG9SignalRNative();
    if (!native) {
      throw new Error(
        'G9SignalRLynxModule is not available: link @g9tm/signalr-supernetcore-lynx into the host (Autolink), or expose ' +
          "createG9SignalRBridge() from the Lynxtron preload ('@g9tm/signalr-supernetcore-lynx/lynxtron').",
      );
    }
    this.url = url;
    this._native = native;
    this._id = `${idPrefix}-${nextId++}`;
    const list = protocols === undefined ? [] : typeof protocols === 'string' ? [protocols] : protocols.slice();

    const headers = { ...ctor.defaultHeaders, ...(options && options.headers ? options.headers : {}) };
    native.wsOpen(this._id, url, list, headers).then(
      () => this._pump(),
      (error: unknown) => this._fail(error),
    );
  }

  get binaryType(): 'arraybuffer' {
    return this._binaryType;
  }

  set binaryType(value: string) {
    if (value !== 'arraybuffer') throw new TypeError(`binaryType '${value}' is not supported on Lynx (no Blob): use 'arraybuffer'.`);
    this._binaryType = value;
  }

  /** Queues a text or binary frame. Throws while connecting; silently drops after close, like a browser. */
  send(data: string | ArrayBuffer | ArrayBufferView): void {
    if (this.readyState === 0) {
      const error = new Error("Failed to execute 'send' on 'WebSocket': Still in CONNECTING state.");
      error.name = 'InvalidStateError';
      throw error;
    }
    if (this.readyState !== 1) return;
    const id = this._id;
    const native = this._native;
    const send =
      typeof data === 'string'
        ? () => native.wsSendText(id, data)
        : (() => {
            const buffer = exactBuffer(data);
            return () => native.wsSendBinary(id, buffer);
          })();
    this._sending = this._sending.then(send).catch((error: unknown) => {
      this._fail(error);
    });
  }

  /** Starts the closing handshake (default 1000). Further sends are dropped. */
  close(code = 1000, reason = ''): void {
    if (this.readyState === 2 || this.readyState === 3) return;
    if (code !== 1000 && (code < 3000 || code > 4999)) {
      const error = new Error(`The close code must be 1000 or in 3000-4999, got ${code}.`);
      error.name = 'InvalidAccessError';
      throw error;
    }
    this.readyState = 2;
    this._native.wsClose(this._id, code, reason).catch((error: unknown) => this._fail(error));
  }

  addEventListener(type: string, listener: Listener): void {
    if (typeof listener !== 'function') return;
    let set = this._listeners.get(type);
    if (!set) this._listeners.set(type, (set = new Set()));
    set.add(listener);
  }

  removeEventListener(type: string, listener: Listener): void {
    this._listeners.get(type)?.delete(listener);
  }

  dispatchEvent(event: G9LynxSocketEvent): boolean {
    const handler = (this as unknown as Record<string, Listener | null>)['on' + event.type];
    const listeners = [...(this._listeners.get(event.type) ?? [])];
    if (typeof handler === 'function') listeners.unshift(handler);
    for (const listener of listeners) {
      try {
        listener.call(this, event);
      } catch (error) {
        // A throwing handler must not stop the socket; surface it like the platform would.
        setTimeout(() => {
          throw error;
        }, 0);
      }
    }
    return true;
  }

  private async _pump(): Promise<void> {
    while (!this._closeFired) {
      let events: G9NativeSocketEvent[];
      try {
        events = await this._native.wsPoll(this._id);
      } catch (error) {
        this._fail(error);
        return;
      }
      for (const event of events) {
        if (this._closeFired) return;
        this._handle(event);
      }
    }
  }

  private _handle(event: G9NativeSocketEvent): void {
    switch (event.type) {
      case 'open':
        if (this.readyState === 0) this.readyState = 1;
        this.protocol = event.protocol ?? '';
        this.dispatchEvent({ type: 'open', target: this });
        break;
      case 'text':
        if (this.readyState === 1 || this.readyState === 2) this.dispatchEvent({ type: 'message', target: this, data: event.data });
        break;
      case 'binary':
        if (this.readyState === 1 || this.readyState === 2) this.dispatchEvent({ type: 'message', target: this, data: event.data });
        break;
      case 'error':
        this.dispatchEvent({ type: 'error', target: this, message: event.message ?? 'WebSocket error.' });
        break;
      case 'close':
        this._fireClose(event.code ?? 1006, event.reason ?? '', event.wasClean === true);
        break;
    }
  }

  private _fail(error: unknown): void {
    if (this._closeFired) return;
    const message = error instanceof Error ? error.message : String(error);
    this.dispatchEvent({ type: 'error', target: this, message });
    // Make sure the native side lets the socket go; its own close event (if any) is no longer delivered.
    this._native.wsClose(this._id, 1000, '').catch(() => undefined);
    this._fireClose(1006, message, false);
  }

  private _fireClose(code: number, reason: string, wasClean: boolean): void {
    if (this._closeFired) return;
    this._closeFired = true;
    this.readyState = 3;
    this.dispatchEvent({ type: 'close', target: this, code, reason, wasClean });
  }
}

/**
 * A WebSocket class bound to `options` (headers for the upgrade request, a specific module). Pass it to SignalR as the
 * `webSocket` option; `lynxClientOptions()` does that for you.
 */
export function createLynxWebSocket(options: G9LynxWebSocketOptions = {}): typeof G9LynxWebSocket {
  const headers = { ...(options.headers ?? {}) };
  const native = options.native;
  return class BoundLynxWebSocket extends G9LynxWebSocket {
    protected static override defaultHeaders = headers;
    protected static override defaultNative = native;
  };
}
