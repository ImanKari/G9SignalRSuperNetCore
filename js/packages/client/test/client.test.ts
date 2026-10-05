import { HubConnectionState, type IRetryPolicy } from '@microsoft/signalr';
import { describe, expect, it } from 'vitest';
import {
  G9Client,
  G9TimeoutError,
  waitUntilConnected,
  type G9ClientOptions,
  type G9ConnectionState,
  type G9ReconnectedInfo,
} from '../src/index.js';
import { FakeHub, sleep, until } from './helpers/fakeHub.js';

function makeClient(hub: FakeHub, options: Partial<G9ClientOptions> = {}) {
  let retryPolicy: IRetryPolicy | undefined;
  const client = new G9Client({
    url: 'http://test/hubs/chat',
    reconnectPolicy: { delaysMs: [0, 5, 5, 5], maxDelayMs: 5, jitter: 0 },
    ...options,
    connectionFactory: (context) => {
      retryPolicy = context.retryPolicy;
      return hub.connection;
    },
  });
  const states: G9ConnectionState[] = [];
  const reconnects: G9ReconnectedInfo[] = [];
  const closes: Array<Error | undefined> = [];
  client.onState((s) => states.push(s));
  client.onReconnected((info) => reconnects.push(info));
  client.onClose((error) => closes.push(error));
  const signalRAsks = (previousRetryCount: number, error = new Error('WebSocket closed with status code: 1006.')) =>
    retryPolicy!.nextRetryDelayInMilliseconds({ previousRetryCount, elapsedMilliseconds: 0, retryReason: error });
  return { client, states, reconnects, closes, signalRAsks };
}

function disconnectedHub(): FakeHub {
  const hub = new FakeHub();
  hub.state = HubConnectionState.Disconnected;
  hub.connectionId = null;
  return hub;
}

describe('G9Client — initial connect', () => {
  it('keeps retrying the initial connect per the policy until it succeeds', async () => {
    const hub = disconnectedHub();
    hub.startImpl = async (attempt) => {
      if (attempt < 3) throw new Error('Failed to fetch (ECONNREFUSED)');
    };
    const { client, states } = makeClient(hub);

    await client.start();

    expect(hub.startCalls).toBe(3);
    expect(client.state).toMatchObject({ phase: 'connected', attempt: 0, connectionId: 'conn-3' });
    expect(client.state.error).toBeUndefined();
    const waits = states.filter((s) => s.nextRetryInMs !== undefined).map((s) => [s.phase, s.attempt, s.nextRetryInMs]);
    expect(waits).toEqual([
      ['connecting', 1, 0],
      ['connecting', 2, 5],
    ]);
    expect(states.find((s) => s.attempt === 1)?.error).toMatch(/ECONNREFUSED/);
    expect(states[0]).toMatchObject({ phase: 'connecting', attempt: 0 });
  });

  it('applies keep-alive and server timeout (defaults 15 s / 30 s)', () => {
    const hub = disconnectedHub();
    makeClient(hub);
    expect(hub.keepAliveIntervalInMilliseconds).toBe(15000);
    expect(hub.serverTimeoutInMilliseconds).toBe(30000);

    const other = disconnectedHub();
    makeClient(other, { keepAliveIntervalMs: 5000, serverTimeoutMs: 12000 });
    expect(other.keepAliveIntervalInMilliseconds).toBe(5000);
    expect(other.serverTimeoutInMilliseconds).toBe(12000);
  });

  it('rejects with the last error when the policy gives up', async () => {
    const hub = disconnectedHub();
    hub.startImpl = async () => {
      throw new Error('401 Unauthorized');
    };
    const { client } = makeClient(hub, { reconnectPolicy: { delaysMs: [1], maxDelayMs: 1, jitter: 0, maxAttempts: 2 } });
    await expect(client.start()).rejects.toThrow('401 Unauthorized');
    expect(hub.startCalls).toBe(3);
    expect(client.state).toMatchObject({ phase: 'disconnected', attempt: 3 });
    expect(client.state.error).toMatch(/401/);
  });

  it('joins a running start, and resolves at once when already connected', async () => {
    const hub = disconnectedHub();
    let release!: () => void;
    hub.startImpl = () => new Promise<void>((resolve) => (release = resolve));
    const { client } = makeClient(hub);
    const first = client.start();
    const second = client.start();
    await until(() => hub.startCalls === 1);
    release();
    await Promise.all([first, second]);
    expect(hub.startCalls).toBe(1);
    await client.start();
    expect(hub.startCalls).toBe(1);
  });

  it('stop() ends the retry loop: start() rejects with an AbortError and no attempt follows', async () => {
    const hub = disconnectedHub();
    hub.startImpl = async () => {
      throw new Error('down');
    };
    const { client } = makeClient(hub, { reconnectPolicy: { delaysMs: [30], maxDelayMs: 30, jitter: 0 } });
    const started = client.start();
    await until(() => hub.startCalls >= 2);
    await client.stop();
    await expect(started).rejects.toMatchObject({ name: 'AbortError' });
    const calls = hub.startCalls;
    await sleep(100);
    expect(hub.startCalls).toBe(calls);
    expect(client.state.phase).toBe('disconnected');
  });

  it('an aborted signal ends the connect attempts', async () => {
    const hub = disconnectedHub();
    hub.startImpl = async () => {
      throw new Error('down');
    };
    const { client } = makeClient(hub, { reconnectPolicy: { delaysMs: [20], maxDelayMs: 20, jitter: 0 } });
    const controller = new AbortController();
    const started = client.start(controller.signal);
    await until(() => hub.startCalls >= 2);
    controller.abort();
    await expect(started).rejects.toMatchObject({ name: 'AbortError' });
    const calls = hub.startCalls;
    await sleep(80);
    expect(hub.startCalls).toBe(calls);
    expect(client.state.phase).toBe('disconnected');
  });
});

describe('G9Client — after the first connect', () => {
  it('reports SignalR automatic reconnect with attempt and next delay, then onReconnected(restarted: false)', async () => {
    const hub = disconnectedHub();
    const { client, states, reconnects, signalRAsks } = makeClient(hub);
    await client.start();

    // SignalR asks the policy for the first delay BEFORE raising onreconnecting.
    expect(signalRAsks(0)).toBe(0);
    hub.simulateReconnecting();
    expect(client.state).toMatchObject({ phase: 'reconnecting', attempt: 0, nextRetryInMs: 0, connectionId: null });
    expect(client.state.error).toMatch(/1006/);

    expect(signalRAsks(1, new Error('Failed to fetch'))).toBe(5);
    expect(client.state).toMatchObject({ phase: 'reconnecting', attempt: 1, nextRetryInMs: 5 });

    hub.simulateReconnected('conn-new');
    expect(client.state).toMatchObject({ phase: 'connected', attempt: 0, connectionId: 'conn-new' });
    expect(reconnects).toEqual([{ connectionId: 'conn-new', restarted: false }]);
    expect(states.filter((s) => s.phase === 'reconnecting').length).toBeGreaterThanOrEqual(2);
  });

  it('restarts after a final close with the same policy and reports restarted: true', async () => {
    const hub = disconnectedHub();
    const { client, reconnects, closes } = makeClient(hub);
    await client.start();

    hub.startImpl = async (attempt) => {
      if (attempt < 4) throw new Error('server restarting');
    };
    const closeError = new Error('Server returned an error on close: Connection closed with an error.');
    hub.simulateClose(closeError);
    expect(closes).toEqual([closeError]);
    expect(client.state.phase).toBe('reconnecting');

    await until(() => client.state.phase === 'connected');
    expect(hub.startCalls).toBe(4);
    expect(reconnects).toEqual([{ connectionId: 'conn-4', restarted: true }]);
  });

  it('does not restart when SignalR reconnect ran out of policy', async () => {
    const hub = disconnectedHub();
    const { client, signalRAsks } = makeClient(hub, { reconnectPolicy: { delaysMs: [0], jitter: 0, maxAttempts: 1 } });
    await client.start();

    expect(signalRAsks(0)).toBe(0);
    hub.simulateReconnecting();
    expect(signalRAsks(1)).toBeNull();
    hub.simulateClose(undefined);

    expect(client.state.phase).toBe('disconnected');
    expect(client.state.error).toMatch(/1006/);
    await sleep(30);
    expect(hub.startCalls).toBe(1);
  });

  it('stop() closes without restarting', async () => {
    const hub = disconnectedHub();
    const { client, closes } = makeClient(hub);
    await client.start();
    await client.stop();
    expect(closes).toEqual([undefined]);
    expect(client.state).toMatchObject({ phase: 'disconnected', attempt: 0, connectionId: null });
    await sleep(30);
    expect(hub.startCalls).toBe(1);
  });

  it('on() returns an unsubscribe for exactly that handler; invoke/send/stream delegate', async () => {
    const hub = new FakeHub();
    const { client } = makeClient(hub);
    const received: unknown[] = [];
    const other: unknown[] = [];
    const off = client.on('Message', (text: string) => received.push(text));
    client.on('Message', (text: string) => other.push(text));
    hub.emit('Message', 'a');
    off();
    hub.emit('Message', 'b');
    expect(received).toEqual(['a']);
    expect(other).toEqual(['a', 'b']);

    hub.methods.set('Add', (a: number, b: number) => a + b);
    expect(await client.invoke<number>('Add', 2, 3)).toBe(5);
    await client.send('Notify', 'x');
    expect(hub.callsOf('Notify')).toEqual([['x']]);
  });
});

describe('waitUntilConnected', () => {
  it('returns at once when connected', async () => {
    const hub = new FakeHub();
    const { client } = makeClient(hub);
    await client.waitUntilConnected(0);
    await waitUntilConnected(hub.connection, 0);
  });

  it('throws a TimeoutError when the connection does not come back in time', async () => {
    const hub = disconnectedHub();
    const { client } = makeClient(hub);
    const error = await client.waitUntilConnected(60).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(G9TimeoutError);
    expect((error as Error).name).toBe('TimeoutError');
  });

  it('completes when SignalR reconnects (raw HubConnection twin of WaitUntilConnectedAsync)', async () => {
    const hub = new FakeHub();
    hub.simulateReconnecting();
    const waiting = waitUntilConnected(hub.connection, 5000);
    setTimeout(() => hub.simulateReconnected('conn-b'), 20);
    await waiting;
    expect(hub.state).toBe(HubConnectionState.Connected);
  });

  it('completes when G9Client restarts the connection', async () => {
    const hub = disconnectedHub();
    const { client } = makeClient(hub);
    const waiting = client.waitUntilConnected(Infinity);
    await client.start();
    await waiting;
  });

  it('rejects with an AbortError, and rejects a negative timeout', async () => {
    const hub = disconnectedHub();
    const controller = new AbortController();
    const waiting = waitUntilConnected(hub.connection, Infinity, controller.signal);
    controller.abort();
    await expect(waiting).rejects.toMatchObject({ name: 'AbortError' });
    await expect(waitUntilConnected(hub.connection, -1)).rejects.toBeInstanceOf(RangeError);
  });
});

describe('G9Client — real HubConnectionBuilder', () => {
  it('builds a disconnected HubConnection with the configured timeouts', () => {
    const client = new G9Client({
      url: 'http://127.0.0.1:9/hubs/chat',
      accessTokenFactory: () => 'token',
      transport: 'websockets',
      headers: { 'X-App': 'g9hub' },
      logLevel: 'none',
      keepAliveIntervalMs: 10000,
      serverTimeoutMs: 40000,
    });
    expect(client.connection.state).toBe(HubConnectionState.Disconnected);
    expect(client.connection.keepAliveIntervalInMilliseconds).toBe(10000);
    expect(client.connection.serverTimeoutInMilliseconds).toBe(40000);
    expect(client.state.phase).toBe('disconnected');
  });

  it('retries a refused initial connect against a real HubConnection until aborted', async () => {
    const client = new G9Client({
      url: 'http://127.0.0.1:9/hubs/chat',
      logLevel: 'none',
      reconnectPolicy: { delaysMs: [0, 10], maxDelayMs: 10, jitter: 0 },
    });
    const states: G9ConnectionState[] = [];
    client.onState((s) => states.push(s));
    const controller = new AbortController();
    const started = client.start(controller.signal);
    await until(() => states.some((s) => s.attempt >= 2), 15000, 'two failed attempts');
    controller.abort();
    await expect(started).rejects.toMatchObject({ name: 'AbortError' });
    expect(states.some((s) => s.phase === 'connecting' && typeof s.error === 'string')).toBe(true);
    await until(() => client.connection.state === HubConnectionState.Disconnected);
  });
});
