import { afterEach, describe, expect, it, vi } from 'vitest';
import { G9ReconnectPolicy } from '../src/index.js';

const context = (previousRetryCount: number, elapsedMilliseconds = 0) => ({
  previousRetryCount,
  elapsedMilliseconds,
  retryReason: new Error('test'),
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('G9ReconnectPolicy — defaults', () => {
  it('follows 0, 1s, 2s, 5s, 10s, 20s, then 30s forever (nominal, jitter off)', () => {
    const policy = new G9ReconnectPolicy({ jitter: 0 });
    const delays = Array.from({ length: 10 }, (_, i) => policy.delayFor(i));
    expect(delays).toEqual([0, 1000, 2000, 5000, 10000, 20000, 30000, 30000, 30000, 30000]);
    expect(policy.delayFor(10_000)).toBe(30000);
  });

  it('never gives up by default, whatever the elapsed time', () => {
    const policy = new G9ReconnectPolicy();
    expect(policy.nextRetryDelayInMilliseconds(context(1_000_000, 365 * 24 * 3600 * 1000))).not.toBeNull();
  });

  it('keeps every jittered delay within ±20% of the nominal delay, and 0 stays 0', () => {
    const policy = new G9ReconnectPolicy();
    const nominal = [0, 1000, 2000, 5000, 10000, 20000, 30000];
    for (let attempt = 0; attempt < nominal.length; attempt++) {
      const expected = nominal[attempt]!;
      const samples = Array.from({ length: 2000 }, () => policy.nextRetryDelayInMilliseconds(context(attempt))!);
      const min = Math.min(...samples);
      const max = Math.max(...samples);
      expect(min).toBeGreaterThanOrEqual(Math.floor(expected * 0.8));
      expect(max).toBeLessThanOrEqual(Math.ceil(expected * 1.2));
      if (expected > 0) {
        // The jitter really spreads the delays (a herd would not).
        expect(max - min).toBeGreaterThan(expected * 0.2);
      } else {
        expect(max).toBe(0);
      }
    }
  });

  it('hits both ends of the jitter range', () => {
    const policy = new G9ReconnectPolicy();
    vi.spyOn(Math, 'random').mockReturnValue(0);
    expect(policy.delayFor(3)).toBe(4000);
    vi.spyOn(Math, 'random').mockReturnValue(0.999999999);
    expect(policy.delayFor(3)).toBe(6000);
  });
});

describe('G9ReconnectPolicy — options', () => {
  it('honours custom delays, cap and maxAttempts', () => {
    const policy = new G9ReconnectPolicy({ delaysMs: [10, 50000], maxDelayMs: 300, jitter: 0, maxAttempts: 4 });
    expect([0, 1, 2, 3].map((i) => policy.delayFor(i))).toEqual([10, 300, 300, 300]);
    expect(policy.delayFor(4)).toBeNull();
    expect(policy.nextRetryDelayInMilliseconds(context(4))).toBeNull();
  });

  it('gives up after maxElapsedMs', () => {
    const policy = new G9ReconnectPolicy({ maxElapsedMs: 1000, jitter: 0 });
    expect(policy.nextRetryDelayInMilliseconds(context(2, 999))).toBe(2000);
    expect(policy.nextRetryDelayInMilliseconds(context(2, 1000))).toBeNull();
  });

  it('rejects invalid options', () => {
    expect(() => new G9ReconnectPolicy({ jitter: 1 })).toThrow(RangeError);
    expect(() => new G9ReconnectPolicy({ delaysMs: [-1] })).toThrow(RangeError);
    expect(() => new G9ReconnectPolicy({ maxDelayMs: -5 })).toThrow(RangeError);
  });

  it('exponential() reproduces the .NET G9CClientReconnectPolicy curve (200 ms ×2, 30 s cap, ±15%, 5 min)', () => {
    const policy = G9ReconnectPolicy.exponential();
    for (let attempt = 0; attempt < 12; attempt++) {
      const nominal = Math.min(200 * 2 ** attempt, 30000);
      for (let i = 0; i < 200; i++) {
        const delay = policy.nextRetryDelayInMilliseconds(context(attempt, 1000))!;
        expect(delay).toBeGreaterThanOrEqual(Math.floor(nominal * 0.85));
        expect(delay).toBeLessThanOrEqual(Math.ceil(nominal * 1.15));
      }
    }
    expect(policy.nextRetryDelayInMilliseconds(context(3, 5 * 60 * 1000))).toBeNull();
  });

  it('fromDelegate() delegates every decision', () => {
    const policy = G9ReconnectPolicy.fromDelegate((ctx) => (ctx.previousRetryCount < 2 ? 42 : null));
    expect(policy.delayFor(0)).toBe(42);
    expect(policy.delayFor(1)).toBe(42);
    expect(policy.delayFor(2)).toBeNull();
  });
});
