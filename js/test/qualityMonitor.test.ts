import { HubConnectionState } from '@microsoft/signalr';
import { afterEach, describe, expect, it } from 'vitest';
import {
  G9Client,
  G9ConnectionQualityMonitor,
  type G9ConnectionQuality,
  type G9ConnectionQualityLevel,
} from '../src/index.js';
import { FakeHub, sleep, until } from './helpers/fakeHub.js';

const monitors: G9ConnectionQualityMonitor[] = [];
afterEach(() => {
  for (const monitor of monitors.splice(0)) monitor.stop();
});

function track(monitor: G9ConnectionQualityMonitor): { monitor: G9ConnectionQualityMonitor; changes: G9ConnectionQualityLevel[] } {
  monitors.push(monitor);
  const changes: G9ConnectionQualityLevel[] = [];
  monitor.onChange((q) => changes.push(q.level));
  return { monitor, changes };
}

/** Scripts G9Ping: each call takes the next outcome ('ok', 'fail', 'hang' or a delay in ms). */
function scriptPing(hub: FakeHub, outcomes: Array<'ok' | 'fail' | 'hang' | number>, fallback: 'ok' | 'fail' = 'ok') {
  let call = 0;
  hub.methods.set('G9Ping', (timestamp: number) => {
    const outcome = outcomes[call++] ?? fallback;
    if (outcome === 'ok') return timestamp;
    if (outcome === 'fail') throw new Error('HubException: G9_RATE_LIMITED');
    if (outcome === 'hang') return new Promise(() => undefined);
    return sleep(outcome).then(() => timestamp);
  });
  return () => call;
}

describe('G9ConnectionQualityMonitor.classify', () => {
  it('uses the .NET thresholds: good < 150 ms, fair < 400 ms, else poor', () => {
    const classify = G9ConnectionQualityMonitor.classify;
    expect([0, 149.9, 150, 399.9, 400, 5000].map(classify)).toEqual(['good', 'good', 'fair', 'fair', 'poor', 'poor']);
    expect(G9ConnectionQualityMonitor.LOST_AFTER_FAILURES).toBe(3);
    expect(G9ConnectionQualityMonitor.DEFAULT_INTERVAL_MS).toBe(5000);
    expect(G9ConnectionQualityMonitor.DEFAULT_PING_METHOD).toBe('G9Ping');
  });
});

describe('G9ConnectionQualityMonitor', () => {
  it('starts as good when connected and lost when not, with rttMs -1', () => {
    const connected = new G9ConnectionQualityMonitor(new FakeHub().connection);
    expect(connected.current).toEqual({ rttMs: -1, level: 'good', measuredAt: 0 });
    const hub = new FakeHub();
    hub.state = HubConnectionState.Disconnected;
    expect(new G9ConnectionQualityMonitor(hub.connection).current.level).toBe('lost');
  });

  it('goes poor after one failure, lost after three in a row, good again on success — raising only on change', async () => {
    const hub = new FakeHub();
    const calls = scriptPing(hub, ['ok', 'fail', 'fail', 'fail', 'fail', 'ok', 'ok']);
    const { monitor, changes } = track(new G9ConnectionQualityMonitor(hub.connection, { intervalMs: 5 }));
    monitor.start();
    await until(() => calls() >= 7, 5000, 'seven probes');
    await until(() => changes.length >= 3);
    expect(changes).toEqual(['poor', 'lost', 'good']);
    expect(monitor.current.level).toBe('good');
    expect(monitor.current.rttMs).toBeGreaterThanOrEqual(0);
    expect(monitor.current.measuredAt).toBeGreaterThan(0);
  });

  it('classifies measured round trips (fair around 200 ms, poor from 400 ms)', async () => {
    const hub = new FakeHub();
    scriptPing(hub, [200, 450]);
    const seen: G9ConnectionQuality[] = [];
    const { monitor } = track(new G9ConnectionQualityMonitor(hub.connection, { intervalMs: 10 }));
    monitor.onChange((q) => seen.push(q));
    monitor.start();
    await until(() => seen.length >= 2, 5000, 'fair then poor');
    expect(seen[0]!.level).toBe('fair');
    expect(seen[0]!.rttMs).toBeGreaterThanOrEqual(190);
    expect(seen[1]!.level).toBe('poor');
    expect(seen[1]!.rttMs).toBeGreaterThanOrEqual(400);
  });

  it('sends an integer timestamp (the server parameter is a long)', async () => {
    const hub = new FakeHub();
    const calls = scriptPing(hub, []);
    const { monitor } = track(new G9ConnectionQualityMonitor(hub.connection, { intervalMs: 1000, pingMethod: 'G9Ping' }));
    monitor.start();
    await until(() => calls() >= 1);
    const [timestamp] = hub.callsOf('G9Ping')[0]!;
    expect(Number.isInteger(timestamp)).toBe(true);
  });

  it('counts a probe that does not answer within the (1 s minimum) timeout as failed', async () => {
    const hub = new FakeHub();
    scriptPing(hub, ['hang']);
    const { monitor, changes } = track(new G9ConnectionQualityMonitor(hub.connection, { intervalMs: 50 }));
    monitor.start();
    await sleep(600);
    expect(changes).toEqual([]);
    await until(() => changes.length >= 1, 3000, 'timeout');
    expect(changes[0]).toBe('poor');
  });

  it('reports lost at once on reconnecting/close and probes at once on reconnected', async () => {
    const hub = new FakeHub();
    const calls = scriptPing(hub, []);
    const { monitor, changes } = track(new G9ConnectionQualityMonitor(hub.connection, { intervalMs: 60000 }));
    monitor.start();
    await until(() => calls() === 1);

    hub.simulateReconnecting();
    expect(changes).toEqual(['lost']);
    expect(monitor.current.rttMs).toBe(-1);

    hub.simulateReconnected('conn-2');
    await until(() => calls() === 2, 2000, 'immediate probe after reconnect');
    await until(() => changes.length === 2);
    expect(changes).toEqual(['lost', 'good']);

    hub.simulateClose(new Error('gone'));
    expect(changes).toEqual(['lost', 'good', 'lost']);
  });

  it('reports lost without probing while the connection is not connected', async () => {
    const hub = new FakeHub();
    const calls = scriptPing(hub, []);
    hub.state = HubConnectionState.Reconnecting;
    const { monitor } = track(new G9ConnectionQualityMonitor(hub.connection, { intervalMs: 5 }));
    monitor.start();
    await sleep(50);
    expect(calls()).toBe(0);
    expect(monitor.current.level).toBe('lost');
  });

  it('stop() ends the probes and detaches from the connection events', async () => {
    const hub = new FakeHub();
    const calls = scriptPing(hub, []);
    const { monitor, changes } = track(new G9ConnectionQualityMonitor(hub.connection, { intervalMs: 5 }));
    monitor.start();
    await until(() => calls() >= 2);
    monitor.stop();
    const count = calls();
    await sleep(50);
    expect(calls()).toBe(count);
    hub.simulateReconnecting();
    expect(changes).toEqual([]);
    expect(monitor.running).toBe(false);
  });

  it('probes at once when a G9Client restarts a closed connection', async () => {
    const hub = new FakeHub();
    hub.state = HubConnectionState.Disconnected;
    const client = new G9Client({
      url: 'http://test/hub',
      reconnectPolicy: { delaysMs: [0], maxDelayMs: 1, jitter: 0 },
      connectionFactory: () => hub.connection,
    });
    await client.start();
    const calls = scriptPing(hub, []);
    const { monitor, changes } = track(new G9ConnectionQualityMonitor(client, { intervalMs: 60000 }));
    monitor.start();
    await until(() => calls() === 1);

    hub.simulateClose(new Error('server restart'));
    expect(changes).toEqual(['lost']);
    await until(() => client.state.phase === 'connected');
    await until(() => calls() === 2, 2000, 'probe after restart');
    await until(() => changes.length === 2);
    expect(changes).toEqual(['lost', 'good']);
    await client.stop();
  });
});
