import {
  HttpTransportType,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection,
  type IHttpConnectionOptions,
  type IHubProtocol,
  type IRetryPolicy,
  type IStreamResult,
  type RetryContext,
} from '@microsoft/signalr';
import { abortErrorFrom, createAbortError } from './errors.js';
import { describeError, safeInvoke, toError } from './internal/async.js';
import { lifecycleOf } from './internal/lifecycle.js';
import { toRetryPolicy, type G9ReconnectPolicy, type G9ReconnectPolicyOptions } from './reconnectPolicy.js';
import { waitUntilConnected as waitForConnection } from './waitUntilConnected.js';

/** Transport selection. `'auto'` (default) lets SignalR negotiate WebSockets → Server-Sent Events → Long Polling. */
export type G9Transport = 'auto' | 'websockets' | 'sse' | 'longpolling';

/** SignalR client log level. */
export type G9LogLevel = 'none' | 'error' | 'warning' | 'information' | 'debug' | 'trace';

/** What {@link G9ClientOptions.connectionFactory} receives. */
export interface G9ConnectionFactoryContext {
  /** The options the client was created with. */
  readonly options: Readonly<G9ClientOptions>;
  /**
   * The retry policy the connection must use for SignalR's automatic reconnect
   * (`withAutomaticReconnect(retryPolicy)`): it is the configured policy, wrapped so the client can report
   * `reconnecting` states with the attempt number and the next delay.
   */
  readonly retryPolicy: IRetryPolicy;
}

/** Options of {@link G9Client}. Twin of the connection-building parameters of the .NET `G9SignalRSuperNetCoreClient`. */
export interface G9ClientOptions {
  /** Hub URL, e.g. `https://host/hubs/chat`. */
  url: string;
  /**
   * Returns the access token (JWT) sent with every connect and reconnect — as the `Authorization` header, or as the
   * `access_token` query parameter where browsers cannot set headers (WebSockets, SSE). Called again for every
   * attempt, so it can refresh an expired token.
   */
  accessTokenFactory?: () => string | Promise<string>;
  /**
   * Reconnect policy for both SignalR's automatic reconnect and the client's own initial-connect / restart loops.
   * Default: a {@link G9ReconnectPolicy} that never gives up (0, 1 s, 2 s, 5 s, 10 s, 20 s, then 30 s; ±20% jitter).
   */
  reconnectPolicy?: G9ReconnectPolicy | IRetryPolicy | G9ReconnectPolicyOptions;
  /**
   * SignalR stateful reconnect (`withStatefulReconnect`): after a short network break the SAME connection resumes
   * without losing messages, provided the server allows it (`AllowStatefulReconnects`). Default `true`.
   */
  statefulReconnect?: boolean;
  /** Stateful-reconnect buffer in bytes; SignalR's default (100,000) when omitted. Twin of `StatefulReconnectBufferSize`. */
  statefulReconnectBufferSize?: number;
  /** Keep-alive ping interval. Default 15000 ms. */
  keepAliveIntervalMs?: number;
  /** Time without any message from the server after which the connection is considered lost. Default 30000 ms. */
  serverTimeoutMs?: number;
  /** Transport restriction. Default `'auto'`. */
  transport?: G9Transport;
  /** Extra HTTP headers (not sent by browsers on WebSockets/SSE, where the platform does not allow it). */
  headers?: Record<string, string>;
  /** Send cookies/credentials with cross-origin requests. SignalR's default (`true`) when omitted. */
  withCredentials?: boolean;
  /** SignalR client log level. Default `'warning'`. */
  logLevel?: G9LogLevel;
  /** Hub protocol; default JSON. Pass `new MessagePackHubProtocol()` for binary frames. */
  protocol?: IHubProtocol;
  /**
   * Last word on the builder before `build()` (twin of the .NET `customConfigureBuilder`). Must keep the automatic
   * reconnect the client configured, or `reconnecting` states and `onReconnected` will not be reported.
   */
  configureBuilder?: (builder: HubConnectionBuilder) => HubConnectionBuilder;
  /**
   * Replaces building the connection altogether (tests, exotic hosts). The returned connection should use
   * `context.retryPolicy` for its automatic reconnect. Keep-alive and server timeout are still applied to it.
   */
  connectionFactory?: (context: G9ConnectionFactoryContext) => HubConnection;
}

/** Connection phase reported by {@link G9Client.state} / {@link G9Client.onState}. */
export type G9ConnectionPhase = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

/** A snapshot of the client's connection state. */
export interface G9ConnectionState {
  /** The current phase. `connecting` = the initial connect loop; `reconnecting` = SignalR reconnect or a restart. */
  phase: G9ConnectionPhase;
  /** Failed attempts so far in the current connect/reconnect cycle (0 on the first try and once connected). */
  attempt: number;
  /** Set while waiting between attempts: the delay before the next one. */
  nextRetryInMs?: number;
  /** Summary of the error that caused the current phase or failed the last attempt. */
  error?: string;
  /** The SignalR connection id while connected, else null. */
  connectionId?: string | null;
  /** When (epoch ms) the client entered the current phase. */
  since: number;
}

/** Payload of {@link G9Client.onReconnected}. */
export interface G9ReconnectedInfo {
  /** The new connection id. */
  connectionId: string | null;
  /**
   * `false`: SignalR's automatic reconnect brought the connection back. `true`: the connection had closed for good
   * (server restart, stateful-reconnect window exceeded, ...) and the client started a new one.
   * In both cases the server sees a NEW connection (SignalR JS resumes a stateful connection silently, without any
   * reconnect event), so per-connection server state such as group membership must be re-established; `true` also
   * means messages may have been lost for longer.
   */
  restarted: boolean;
}

type StateInput = {
  phase: G9ConnectionPhase;
  attempt: number;
  nextRetryInMs?: number;
  error?: string;
  connectionId?: string | null;
};

interface Waiter {
  resolve(): void;
  reject(error: unknown): void;
}

interface LoopHandle {
  cancel(reason: Error): void;
}

const LOG_LEVELS: Record<G9LogLevel, LogLevel> = {
  none: LogLevel.None,
  error: LogLevel.Error,
  warning: LogLevel.Warning,
  information: LogLevel.Information,
  debug: LogLevel.Debug,
  trace: LogLevel.Trace,
};

function toTransport(transport: G9Transport | undefined): HttpTransportType | undefined {
  switch (transport) {
    case 'websockets':
      return HttpTransportType.WebSockets;
    case 'sse':
      return HttpTransportType.ServerSentEvents;
    case 'longpolling':
      return HttpTransportType.LongPolling;
    case undefined:
    case 'auto':
      return undefined;
    default:
      throw new RangeError(`Unknown transport '${String(transport)}'.`);
  }
}

/** Builds the HubConnection for {@link G9Client} (also used by `authorize`). */
export function buildHubConnection(
  options: Pick<
    G9ClientOptions,
    | 'url'
    | 'accessTokenFactory'
    | 'transport'
    | 'headers'
    | 'withCredentials'
    | 'logLevel'
    | 'protocol'
    | 'statefulReconnect'
    | 'statefulReconnectBufferSize'
    | 'configureBuilder'
  >,
  retryPolicy?: IRetryPolicy,
): HubConnection {
  if (!options.url) throw new TypeError('A hub URL is required.');
  const http: IHttpConnectionOptions = {};
  if (options.accessTokenFactory) http.accessTokenFactory = options.accessTokenFactory;
  const transport = toTransport(options.transport);
  if (transport !== undefined) http.transport = transport;
  if (options.headers) http.headers = { ...options.headers };
  if (options.withCredentials !== undefined) http.withCredentials = options.withCredentials;

  let builder = new HubConnectionBuilder()
    .withUrl(options.url, http)
    .configureLogging(LOG_LEVELS[options.logLevel ?? 'warning'] ?? LogLevel.Warning);
  if (retryPolicy) builder = builder.withAutomaticReconnect(retryPolicy);
  if (options.protocol) builder = builder.withHubProtocol(options.protocol);
  if (options.statefulReconnect ?? true) {
    builder =
      options.statefulReconnectBufferSize !== undefined
        ? builder.withStatefulReconnect({ bufferSize: options.statefulReconnectBufferSize })
        : builder.withStatefulReconnect();
  }
  if (options.configureBuilder) builder = options.configureBuilder(builder);
  return builder.build();
}

/**
 * A resilient SignalR hub client. Twin of the .NET `G9SignalRSuperNetCoreClient`.
 *
 * Beyond a bare HubConnection it
 * - keeps retrying the INITIAL connect per the reconnect policy (SignalR's automatic reconnect only covers drops after
 *   a first successful connect),
 * - restarts the connection with the same policy after a final `close` (server restart, reconnect window exceeded),
 *   reporting it through {@link onReconnected} with `restarted: true`,
 * - reports one consolidated {@link G9ConnectionState} (phase, attempt, next retry delay, error).
 */
export class G9Client {
  /** The underlying SignalR connection (the same instance for the client's whole life). */
  readonly connection: HubConnection;

  private readonly _policy: IRetryPolicy;
  private _state: G9ConnectionState;
  private readonly _stateListeners = new Set<(state: G9ConnectionState) => void>();
  private readonly _reconnectedListeners = new Set<(info: G9ReconnectedInfo) => void>();
  private readonly _closeListeners = new Set<(error?: Error) => void>();
  private readonly _waiters = new Set<Waiter>();
  private _waiterPoll: ReturnType<typeof setInterval> | undefined;
  private _wantConnected = false;
  private _loop: LoopHandle | null = null;
  private _signalRGaveUp: Error | null = null;
  private _pendingReconnecting: StateInput | null = null;
  private _stopPromise: Promise<void> | null = null;

  constructor(options: G9ClientOptions) {
    if (!options) throw new TypeError('Options are required.');
    if (!options.url && !options.connectionFactory) throw new TypeError('A hub URL is required.');
    const frozen: Readonly<G9ClientOptions> = Object.freeze({ ...options });
    this._policy = toRetryPolicy(options.reconnectPolicy);

    const tracked: IRetryPolicy = {
      nextRetryDelayInMilliseconds: (context) => this._onSignalRRetry(context),
    };
    this.connection = options.connectionFactory
      ? options.connectionFactory({ options: frozen, retryPolicy: tracked })
      : buildHubConnection(frozen, tracked);
    this.connection.keepAliveIntervalInMilliseconds = options.keepAliveIntervalMs ?? 15000;
    this.connection.serverTimeoutInMilliseconds = options.serverTimeoutMs ?? 30000;

    this._state = Object.freeze({ phase: 'disconnected', attempt: 0, connectionId: null, since: Date.now() });

    const lifecycle = lifecycleOf(this.connection);
    lifecycle.onReconnecting((error) => this._handleReconnecting(error));
    lifecycle.onReconnected((connectionId) => this._handleReconnected(connectionId));
    lifecycle.onClose((error) => this._handleClose(error));
  }

  /** The current state snapshot (immutable). */
  get state(): G9ConnectionState {
    return this._state;
  }

  /** Subscribes to state changes; returns the unsubscribe function. Not called with the current state. */
  onState(callback: (state: G9ConnectionState) => void): () => void {
    this._stateListeners.add(callback);
    return () => void this._stateListeners.delete(callback);
  }

  /**
   * Connects. Resolves once connected; while it is not, it keeps retrying per the reconnect policy (so a server that
   * is down at app start is picked up when it comes back). Rejects when the policy gives up, when {@link stop} is
   * called (`AbortError`), or when `signal` aborts (which also ends the connect attempts). Calling it while already
   * connected resolves at once; calling it again while connecting joins the running attempt.
   */
  start(signal?: AbortSignal): Promise<void> {
    if (signal?.aborted) return Promise.reject(abortErrorFrom(signal));
    this._wantConnected = true;

    if (this.connection.state === HubConnectionState.Connected) {
      if (this._state.phase !== 'connected') this._setState({ phase: 'connected', attempt: 0 });
      return Promise.resolve();
    }

    const promise = this._addWaiter(signal);
    const state = this.connection.state;
    if (!this._loop && (state === HubConnectionState.Disconnected || state === HubConnectionState.Disconnecting)) {
      this._startLoop('initial');
    }
    return promise;
  }

  /** Stops the connection and every connect/restart attempt. Pending {@link start} calls reject with an `AbortError`. */
  async stop(): Promise<void> {
    this._wantConnected = false;
    const reason = createAbortError('The connection was stopped.');
    this._loop?.cancel(reason);
    this._loop = null;
    this._settleWaiters(reason);

    const stopping = this.connection.stop();
    this._stopPromise = stopping;
    try {
      await stopping;
    } finally {
      if (this._stopPromise === stopping) this._stopPromise = null;
      if (!this._wantConnected) this._setState({ phase: 'disconnected', attempt: 0 });
    }
  }

  /** Invokes a hub method and resolves with its result. */
  invoke<T = unknown>(method: string, ...args: unknown[]): Promise<T> {
    return this.connection.invoke<T>(method, ...args);
  }

  /** Sends a hub invocation without waiting for its completion. */
  send(method: string, ...args: unknown[]): Promise<void> {
    return this.connection.send(method, ...args);
  }

  /** Registers a handler for a server-to-client method; returns the function that removes exactly this handler. */
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  on(method: string, handler: (...args: any[]) => void): () => void {
    this.connection.on(method, handler);
    return () => this.connection.off(method, handler);
  }

  /** Starts a server-to-client stream. */
  stream<T>(method: string, ...args: unknown[]): IStreamResult<T> {
    return this.connection.stream<T>(method, ...args);
  }

  /**
   * Waits until the connection is connected (at once if it is). Does not start it. Rejects with a `TimeoutError`
   * after `timeoutMs` (`Infinity` waits forever) and with an `AbortError` when `signal` aborts. Twin of the .NET
   * `WaitUntilConnectedAsync`.
   */
  waitUntilConnected(timeoutMs: number, signal?: AbortSignal): Promise<void> {
    return waitForConnection(this.connection, timeoutMs, signal, (wake) =>
      this.onState((state) => {
        if (state.phase === 'connected') wake();
      }),
    );
  }

  /**
   * Called after SignalR's automatic reconnect (`restarted: false`) and after the client restarted a connection that
   * had closed for good (`restarted: true`). Resynchronize server-side state here. Returns the unsubscribe function.
   */
  onReconnected(callback: (info: G9ReconnectedInfo) => void): () => void {
    this._reconnectedListeners.add(callback);
    return () => void this._reconnectedListeners.delete(callback);
  }

  /**
   * Called whenever the underlying connection closes: after {@link stop} (no error), or on a final failure (an error),
   * which the client answers with a restart unless the policy gave up. Returns the unsubscribe function.
   */
  onClose(callback: (error?: Error) => void): () => void {
    this._closeListeners.add(callback);
    return () => void this._closeListeners.delete(callback);
  }

  // ---- internals ---------------------------------------------------------------------------------------------------

  private _nextDelay(previousRetryCount: number, elapsedMilliseconds: number, reason: unknown): number | null {
    try {
      const delay = this._policy.nextRetryDelayInMilliseconds({
        previousRetryCount,
        elapsedMilliseconds,
        retryReason: toError(reason),
      });
      return delay === null || delay === undefined ? null : Math.max(0, delay);
    } catch {
      return null;
    }
  }

  private _onSignalRRetry(context: RetryContext): number | null {
    const delay = this._nextDelay(context.previousRetryCount, context.elapsedMilliseconds, context.retryReason);
    if (delay === null) {
      this._signalRGaveUp = context.retryReason ?? new Error('Reconnect attempts exhausted.');
      return null;
    }

    const next: StateInput = {
      phase: 'reconnecting',
      attempt: context.previousRetryCount,
      nextRetryInMs: delay,
      error: context.retryReason ? describeError(context.retryReason) : undefined,
    };
    // SignalR asks for the first delay BEFORE it raises onreconnecting; later delays come while already reconnecting.
    if (this._state.phase === 'reconnecting') this._setState(next);
    else this._pendingReconnecting = next;
    return delay;
  }

  private _handleReconnecting(error?: Error): void {
    const pending = this._pendingReconnecting;
    this._pendingReconnecting = null;
    this._setState(pending ?? { phase: 'reconnecting', attempt: 0, error: error ? describeError(error) : undefined });
  }

  private _handleReconnected(connectionId?: string): void {
    this._signalRGaveUp = null;
    this._pendingReconnecting = null;
    const id = connectionId ?? this.connection.connectionId ?? null;
    this._setState({ phase: 'connected', attempt: 0, connectionId: id });
    this._settleWaiters();
    this._emitReconnected({ connectionId: id, restarted: false });
  }

  private _handleClose(error?: Error): void {
    const gaveUp = this._signalRGaveUp;
    this._signalRGaveUp = null;
    this._pendingReconnecting = null;

    if (!this._wantConnected) {
      this._setState({ phase: 'disconnected', attempt: 0, error: error ? describeError(error) : undefined });
      this._emitClose(error);
      return;
    }

    if (gaveUp) {
      // SignalR's automatic reconnect ran out of policy: do not start another cycle with the same policy.
      this._wantConnected = false;
      const reason = error ?? gaveUp;
      this._setState({ phase: 'disconnected', attempt: 0, error: describeError(reason) });
      this._emitClose(reason);
      this._settleWaiters(reason);
      return;
    }

    // A final close the policy did not decide (server restart, stateful-reconnect window exceeded, server Close
    // message): restart with the same policy.
    this._setState({ phase: 'reconnecting', attempt: 0, error: error ? describeError(error) : undefined });
    this._emitClose(error);
    this._startLoop('restart');
  }

  private _startLoop(kind: 'initial' | 'restart'): void {
    if (this._loop) return;
    const phase: G9ConnectionPhase = kind === 'initial' ? 'connecting' : 'reconnecting';
    let cancelled: Error | null = null;
    let wake: (() => void) | null = null;
    let timer: ReturnType<typeof setTimeout> | undefined;
    let attempt = 0;

    const handle: LoopHandle = {
      cancel: (reason) => {
        if (cancelled) return;
        cancelled = reason;
        if (timer !== undefined) clearTimeout(timer);
        const resume = wake;
        wake = null;
        resume?.();
      },
    };
    this._loop = handle;

    // Outcomes are handled synchronously in the continuation of connection.start(), before any other task can run: a
    // close event arriving right after a successful start must already see the loop finished (and restart it).
    const run = async (): Promise<void> => {
      const startedAt = Date.now();
      let lastError: unknown;
      const pendingStop = this._stopPromise;
      if (pendingStop) await pendingStop.catch(() => undefined);

      for (;;) {
        if (cancelled) return; // stop() or an aborted start() already settled everything.
        this._setState({ phase, attempt, error: lastError === undefined ? undefined : describeError(lastError) });
        try {
          await this.connection.start();
        } catch (error) {
          if (cancelled) return;
          lastError = error;
          const delay = this._nextDelay(attempt, Date.now() - startedAt, error);
          attempt++;
          if (delay === null) {
            // The policy gave up.
            if (this._loop === handle) this._loop = null;
            this._wantConnected = false;
            this._setState({ phase: 'disconnected', attempt, error: describeError(error) });
            this._settleWaiters(error);
            return;
          }
          this._setState({ phase, attempt, nextRetryInMs: delay, error: describeError(error) });
          await new Promise<void>((resolve) => {
            wake = resolve;
            timer = setTimeout(resolve, delay);
          });
          wake = null;
          timer = undefined;
          continue;
        }
        if (cancelled) return; // stop() awaits the start it interrupted and takes the connection down itself.

        if (this._loop === handle) this._loop = null;
        this._signalRGaveUp = null;
        const connectionId = this.connection.connectionId ?? null;
        this._setState({ phase: 'connected', attempt: 0, connectionId });
        this._settleWaiters();
        if (kind === 'restart') this._emitReconnected({ connectionId, restarted: true });
        return;
      }
    };

    run().catch(() => undefined);
  }

  private _addWaiter(signal?: AbortSignal): Promise<void> {
    return new Promise<void>((resolve, reject) => {
      const onAbort = (): void => {
        this._waiters.delete(waiter);
        const reason = abortErrorFrom(signal);
        // An aborted start() ends the connect attempts it asked for, unless another start() is still waiting.
        if (this._waiters.size === 0 && this._loop && this.connection.state !== HubConnectionState.Connected) {
          this._wantConnected = false;
          this._loop.cancel(reason);
          this._loop = null;
          this.connection.stop().catch(() => undefined);
          this._setState({ phase: 'disconnected', attempt: 0 });
        }
        this._updateWaiterPoll();
        reject(reason);
      };
      const waiter: Waiter = {
        resolve: () => {
          signal?.removeEventListener('abort', onAbort);
          resolve();
        },
        reject: (error) => {
          signal?.removeEventListener('abort', onAbort);
          reject(error);
        },
      };
      signal?.addEventListener('abort', onAbort, { once: true });
      this._waiters.add(waiter);
      this._updateWaiterPoll();
    });
  }

  /**
   * While someone waits in start(), polls the connection state as a safety net (e.g. a connect started directly on
   * `connection` raises no event the client could observe).
   */
  private _updateWaiterPoll(): void {
    if (this._waiters.size > 0 && this._waiterPoll === undefined) {
      this._waiterPoll = setInterval(() => {
        if (this.connection.state === HubConnectionState.Connected && !this._loop) {
          if (this._state.phase !== 'connected') this._setState({ phase: 'connected', attempt: 0 });
          this._settleWaiters();
        }
      }, 250);
    } else if (this._waiters.size === 0 && this._waiterPoll !== undefined) {
      clearInterval(this._waiterPoll);
      this._waiterPoll = undefined;
    }
  }

  private _settleWaiters(error?: unknown): void {
    const waiters = [...this._waiters];
    this._waiters.clear();
    this._updateWaiterPoll();
    for (const waiter of waiters) {
      if (error === undefined) waiter.resolve();
      else waiter.reject(error);
    }
  }

  private _setState(input: StateInput): void {
    const previous = this._state;
    const connectionId =
      input.connectionId !== undefined
        ? input.connectionId
        : input.phase === 'connected'
          ? (this.connection.connectionId ?? null)
          : null;
    const next: G9ConnectionState = {
      phase: input.phase,
      attempt: input.attempt,
      connectionId,
      since: previous.phase === input.phase ? previous.since : Date.now(),
    };
    if (input.nextRetryInMs !== undefined) next.nextRetryInMs = input.nextRetryInMs;
    if (input.error !== undefined) next.error = input.error;

    if (
      previous.phase === next.phase &&
      previous.attempt === next.attempt &&
      previous.nextRetryInMs === next.nextRetryInMs &&
      previous.error === next.error &&
      previous.connectionId === next.connectionId
    ) {
      return;
    }

    this._state = Object.freeze(next);
    for (const listener of [...this._stateListeners]) safeInvoke(listener, this._state);
  }

  private _emitReconnected(info: G9ReconnectedInfo): void {
    for (const listener of [...this._reconnectedListeners]) safeInvoke(listener, info);
  }

  private _emitClose(error?: Error): void {
    for (const listener of [...this._closeListeners]) safeInvoke(listener, error);
  }
}
