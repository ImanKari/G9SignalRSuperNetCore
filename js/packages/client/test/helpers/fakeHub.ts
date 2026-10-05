/* eslint-disable @typescript-eslint/no-explicit-any */
import {
  HubConnectionState,
  JsonHubProtocol,
  MessageType,
  NullLogger,
  Subject,
  type HubConnection,
  type IHubProtocol,
  type IStreamResult,
  type StreamItemMessage,
} from '@microsoft/signalr';

type Handler = (...args: any[]) => unknown;

/**
 * A HubConnection double: records invocations, lets tests script hub methods and raise lifecycle events. It exposes
 * the protocol as a public `protocol` property, which the library reads before the private `_protocol`.
 */
export class FakeHub {
  state: HubConnectionState = HubConnectionState.Connected;
  connectionId: string | null = 'conn-0';
  keepAliveIntervalInMilliseconds = 0;
  serverTimeoutInMilliseconds = 0;
  readonly protocol: IHubProtocol;

  readonly handlers = new Map<string, Handler[]>();
  readonly methods = new Map<string, Handler>();
  readonly streams = new Map<string, (...args: any[]) => IStreamResult<unknown>>();
  readonly calls: Array<{ kind: 'invoke' | 'send' | 'stream'; method: string; args: unknown[] }> = [];

  private readonly closeCallbacks: Array<(error?: Error) => void> = [];
  private readonly reconnectingCallbacks: Array<(error?: Error) => void> = [];
  private readonly reconnectedCallbacks: Array<(connectionId?: string) => void> = [];

  /** Scripted behaviour of start(); default succeeds. */
  startImpl: (attempt: number) => Promise<void> = async () => undefined;
  startCalls = 0;
  stopCalls = 0;

  constructor(protocol: IHubProtocol = new JsonHubProtocol()) {
    this.protocol = protocol;
  }

  get connection(): HubConnection {
    return this as unknown as HubConnection;
  }

  // ---- HubConnection surface ----

  on(method: string, handler: Handler): void {
    const key = method.toLowerCase();
    const list = this.handlers.get(key) ?? [];
    list.push(handler);
    this.handlers.set(key, list);
  }

  off(method: string, handler?: Handler): void {
    const key = method.toLowerCase();
    if (!handler) {
      this.handlers.delete(key);
      return;
    }
    const list = (this.handlers.get(key) ?? []).filter((h) => h !== handler);
    if (list.length) this.handlers.set(key, list);
    else this.handlers.delete(key);
  }

  onclose(callback: (error?: Error) => void): void {
    this.closeCallbacks.push(callback);
  }

  onreconnecting(callback: (error?: Error) => void): void {
    this.reconnectingCallbacks.push(callback);
  }

  onreconnected(callback: (connectionId?: string) => void): void {
    this.reconnectedCallbacks.push(callback);
  }

  async start(): Promise<void> {
    if (this.state !== HubConnectionState.Disconnected) {
      throw new Error("Cannot start a HubConnection that is not in the 'Disconnected' state.");
    }
    this.startCalls++;
    this.state = HubConnectionState.Connecting;
    try {
      await this.startImpl(this.startCalls);
    } catch (error) {
      this.state = HubConnectionState.Disconnected;
      throw error;
    }
    if (this.state !== HubConnectionState.Connecting) throw new Error('The connection was stopped during negotiation.');
    this.state = HubConnectionState.Connected;
    this.connectionId = `conn-${this.startCalls}`;
  }

  async stop(): Promise<void> {
    this.stopCalls++;
    const was = this.state;
    this.state = HubConnectionState.Disconnected;
    this.connectionId = null;
    if (was === HubConnectionState.Connected || was === HubConnectionState.Reconnecting) {
      for (const callback of [...this.closeCallbacks]) callback(undefined);
    }
  }

  invoke<T = any>(method: string, ...args: unknown[]): Promise<T> {
    this.calls.push({ kind: 'invoke', method, args });
    if (this.state !== HubConnectionState.Connected) {
      return Promise.reject(new Error("Cannot send data if the connection is not in the 'Connected' State."));
    }
    const handler = this.methods.get(method);
    if (!handler) return Promise.reject(new Error(`Method does not exist: ${method}`));
    try {
      return Promise.resolve(handler(...args) as T);
    } catch (error) {
      return Promise.reject(error);
    }
  }

  send(method: string, ...args: unknown[]): Promise<void> {
    this.calls.push({ kind: 'send', method, args });
    const handler = this.methods.get(method);
    if (handler) {
      try {
        void Promise.resolve(handler(...args)).catch(() => undefined);
      } catch {
        // ignore
      }
    }
    return Promise.resolve();
  }

  stream<T = any>(method: string, ...args: unknown[]): IStreamResult<T> {
    this.calls.push({ kind: 'stream', method, args });
    const factory = this.streams.get(method);
    if (!factory) {
      const subject = new Subject<T>();
      setTimeout(() => subject.error(new Error(`Stream method does not exist: ${method}`)), 0);
      return subject;
    }
    return factory(...args) as IStreamResult<T>;
  }

  // ---- test controls ----

  /** Calls the client handlers registered for `method` (a server-to-client call). */
  emit(method: string, ...args: unknown[]): void {
    for (const handler of [...(this.handlers.get(method.toLowerCase()) ?? [])]) handler(...args);
  }

  handlerCount(method: string): number {
    return this.handlers.get(method.toLowerCase())?.length ?? 0;
  }

  simulateReconnecting(error = new Error('WebSocket closed with status code: 1006.')): void {
    this.state = HubConnectionState.Reconnecting;
    this.connectionId = null;
    for (const callback of [...this.reconnectingCallbacks]) callback(error);
  }

  simulateReconnected(connectionId = 'conn-re'): void {
    this.state = HubConnectionState.Connected;
    this.connectionId = connectionId;
    for (const callback of [...this.reconnectedCallbacks]) callback(connectionId);
  }

  simulateClose(error?: Error): void {
    this.state = HubConnectionState.Disconnected;
    this.connectionId = null;
    for (const callback of [...this.closeCallbacks]) callback(error);
  }

  callsOf(method: string): unknown[][] {
    return this.calls.filter((c) => c.method === method).map((c) => c.args);
  }

  /**
   * Serializes a stream item exactly as SignalR does (protocol.writeMessage of a StreamItem message) and parses it
   * back, returning what the server would receive as the item.
   */
  roundTripStreamItem(item: unknown): unknown {
    const message: StreamItemMessage = { type: MessageType.StreamItem, invocationId: '1', item };
    const wire = this.protocol.writeMessage(message);
    const parsed = this.protocol.parseMessages(wire, NullLogger.instance);
    const first = parsed[0] as StreamItemMessage | undefined;
    if (!first || first.type !== MessageType.StreamItem) throw new Error('Round trip did not produce a StreamItem.');
    return first.item;
  }
}

/**
 * Byte-exact comparison that stays fast for multi-MiB arrays (a deep `toEqual` on a large Uint8Array walks every
 * index as an object key and takes seconds).
 */
export function sameBytes(actual: Uint8Array, expected: Uint8Array): boolean {
  return (
    actual.length === expected.length &&
    Buffer.from(actual.buffer, actual.byteOffset, actual.byteLength).equals(
      Buffer.from(expected.buffer, expected.byteOffset, expected.byteLength),
    )
  );
}

/** Resolves after `ms` (real timers). */
export function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/** Waits until `predicate` holds (polling), or fails after `timeoutMs`. */
export async function until(predicate: () => boolean, timeoutMs = 5000, what = 'condition'): Promise<void> {
  const started = Date.now();
  while (!predicate()) {
    if (Date.now() - started > timeoutMs) throw new Error(`Timed out waiting for ${what}.`);
    await sleep(2);
  }
}
