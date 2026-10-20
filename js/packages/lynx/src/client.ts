import {
  authorize,
  G9Client,
  type G9AuthorizeOptions,
  type G9AuthorizeResult,
  type G9ClientOptions,
} from '@g9tm/signalr-supernetcore-client';
import type { HttpClient, HttpRequest, HttpResponse } from '@microsoft/signalr';
import { getG9SignalRNative, type G9SignalRNative } from './native.js';
import { installUrlShim } from './url.js';
import { createLynxWebSocket } from './webSocket.js';

declare const fetch: unknown;

/** How a Lynx connection reaches the server, as chosen by {@link lynxClientOptions}. */
export type G9LynxTransportChoice =
  /** The G9 native WebSocket (binary frames), straight to the hub without negotiating. */
  | 'native-websocket'
  /** The G9 native WebSocket after a negotiate request (`skipNegotiation: false`, e.g. behind Azure SignalR). */
  | 'native-websocket-negotiated'
  /**
   * Negotiated: the G9 native WebSocket when the server offers WebSockets and it opens, else HTTP long polling over
   * Lynx's `fetch` (`longPollingFallback: true`).
   */
  | 'native-websocket-or-long-polling'
  /** The runtime's own WebSocket (Lynx for Web, a browser). */
  | 'platform'
  /** No WebSocket at all: HTTP long polling over Lynx's `fetch`. */
  | 'long-polling';

/** Options of {@link lynxClientOptions} / {@link createLynxClient}: {@link G9ClientOptions} plus the Lynx knobs. */
export interface G9LynxClientOptions extends G9ClientOptions {
  /** The platform module; default detected (`getG9SignalRNative()`). `null` forces the non-native paths. */
  native?: G9SignalRNative | null;
  /** Reports which way the connection goes (for diagnostics). */
  onTransportChosen?: (choice: G9LynxTransportChoice) => void;
  /**
   * With the native module: negotiate, and fall back to HTTP long polling over `fetch` when the server does not offer
   * WebSockets or the WebSocket cannot be opened (a server whose host lacks WebSockets — IIS without the WebSocket
   * feature — or a proxy that refuses the upgrade). Costs the negotiate round trip on every connect. Default `false`
   * (WebSockets straight, no negotiation). Ignored with an explicit `transport` or `skipNegotiation: true`.
   */
  longPollingFallback?: boolean;
}

/** An HttpClient for connections that never make an HTTP request (WebSockets without negotiation, no `fetch`). */
class NoHttpClient {
  get(url: string): Promise<HttpResponse> {
    return this.send({ method: 'GET', url });
  }
  post(url: string): Promise<HttpResponse> {
    return this.send({ method: 'POST', url });
  }
  delete(url: string): Promise<HttpResponse> {
    return this.send({ method: 'DELETE', url });
  }
  send(request: HttpRequest): Promise<HttpResponse> {
    return Promise.reject(new Error(`This Lynx host has no HTTP service (fetch), so ${request.method} ${request.url} cannot be sent.`));
  }
  getCookieString(): string {
    return '';
  }
}

/**
 * The {@link G9ClientOptions} that make a hub connection work on Lynx's background thread:
 * - with the G9 native module: the native WebSocket (text + binary, so MessagePack works), `transport: 'websockets'`
 *   and `skipNegotiation: true` (one round trip less, and no `URL` API needed). Pass `skipNegotiation: false` to keep
 *   negotiating (the URL shim is installed then);
 * - without it but with a platform WebSocket (Lynx for Web): the platform's, negotiating as usual;
 * - with neither: long polling over `fetch` (slower; JSON or MessagePack).
 * `headers` are also sent with the WebSocket upgrade, which a browser cannot do.
 */
export function lynxClientOptions(options: G9LynxClientOptions): G9ClientOptions {
  const { native: requested, onTransportChosen, longPollingFallback, ...rest } = options;
  const native = requested === null ? null : (requested ?? getG9SignalRNative());
  const g = globalThis as Record<string, unknown>;
  const out: G9ClientOptions = { ...rest };
  let choice: G9LynxTransportChoice;

  if (native && out.webSocket === undefined) {
    out.webSocket = createLynxWebSocket({ headers: rest.headers, native });
    const fallback = longPollingFallback === true && rest.transport === undefined && rest.skipNegotiation !== true;
    out.transport = fallback ? 'websockets-or-longpolling' : 'websockets';
    if (fallback) {
      installUrlShim();
      out.skipNegotiation = false;
      choice = 'native-websocket-or-long-polling';
    } else if (rest.skipNegotiation === false) {
      installUrlShim();
      choice = 'native-websocket-negotiated';
    } else {
      out.skipNegotiation = true;
      choice = 'native-websocket';
    }
  } else if (out.webSocket !== undefined || typeof g.WebSocket === 'function') {
    installUrlShim();
    choice = 'platform';
  } else {
    installUrlShim();
    out.transport = 'longpolling';
    out.skipNegotiation = false;
    choice = 'long-polling';
  }

  // SignalR builds an HTTP client even when it will never use one; without `fetch` that throws "No usable HttpClient".
  if (out.httpClient === undefined && typeof fetch === 'undefined' && typeof g.XMLHttpRequest !== 'function') {
    if (out.skipNegotiation) out.httpClient = new NoHttpClient() as unknown as HttpClient;
    else throw new Error('This Lynx host has no HTTP service (fetch) and no native WebSocket: nothing can reach the hub.');
  }

  if (onTransportChosen) onTransportChosen(choice);
  return out;
}

/** A {@link G9Client} configured by {@link lynxClientOptions}. */
export function createLynxClient(options: G9LynxClientOptions): G9Client {
  return new G9Client(lynxClientOptions(options));
}

/** `authorize()` (the JWT route) with the same Lynx transport choice as {@link lynxClientOptions}. */
export function lynxAuthorize(
  authHubUrl: string,
  authorizeData: unknown,
  options: G9AuthorizeOptions & { native?: G9SignalRNative | null } = {},
): Promise<G9AuthorizeResult> {
  const { native, ...rest } = options;
  const resolved = lynxClientOptions({
    url: authHubUrl,
    native,
    headers: rest.headers,
    transport: rest.transport,
    webSocket: rest.webSocket,
    skipNegotiation: rest.skipNegotiation,
    httpClient: rest.httpClient,
  });
  return authorize(authHubUrl, authorizeData, {
    ...rest,
    transport: resolved.transport,
    webSocket: resolved.webSocket,
    skipNegotiation: resolved.skipNegotiation,
    httpClient: resolved.httpClient,
  });
}
