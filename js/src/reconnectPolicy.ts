import type { IRetryPolicy, RetryContext } from '@microsoft/signalr';

/** Tuning for {@link G9ReconnectPolicy}. Every field is optional; the defaults never give up. */
export interface G9ReconnectPolicyOptions {
  /**
   * Nominal delay before each retry, by retry index (the first entry is used for the first retry). Every entry is
   * capped by {@link maxDelayMs}, and once the list runs out {@link maxDelayMs} is used for every further retry.
   * Default `[0, 1000, 2000, 5000, 10000, 20000]`.
   */
  delaysMs?: number[];
  /** Upper bound of a nominal delay, and the delay used after {@link delaysMs} runs out. Default `30000`. */
  maxDelayMs?: number;
  /** Relative jitter applied to every nominal delay: 0.2 gives a delay in [0.8, 1.2] × nominal. Default `0.2`. */
  jitter?: number;
  /** Give up after this many retries (the retry with index `maxAttempts` is not made). Default: never. */
  maxAttempts?: number;
  /** Give up once this much time has passed since the reconnect cycle began. Default: never. */
  maxElapsedMs?: number;
}

/** Options of {@link G9ReconnectPolicy.exponential}, the curve of the .NET `G9CClientReconnectPolicy`. */
export interface G9ExponentialReconnectOptions {
  /** First retry delay. Default 200 ms. */
  baseDelayMs?: number;
  /** Multiplier between consecutive retries. Default 2. */
  factor?: number;
  /** Upper bound of a single (nominal) delay. Default 30 s. */
  maxDelayMs?: number;
  /** Give up after this much elapsed time; `Infinity` retries forever. Default 5 minutes. */
  maxElapsedMs?: number;
  /** Relative jitter. Default 0.15 (±15%), like .NET. */
  jitter?: number;
}

type Delayer = (context: RetryContext) => number | null;

const DEFAULT_DELAYS_MS: readonly number[] = Object.freeze([0, 1000, 2000, 5000, 10000, 20000]);

/**
 * Reconnect policy for `HubConnectionBuilder.withAutomaticReconnect()` and for {@link G9Client}'s own initial-connect
 * and restart loops. Twin of the .NET `G9CClientReconnectPolicy`.
 *
 * The default schedule is 0, 1 s, 2 s, 5 s, 10 s, 20 s, then 30 s forever, each with ±20% jitter so that many clients
 * dropped by the same outage do not reconnect in lockstep. SignalR's own default (0, 2, 10, 30 s, then give up) is not
 * suitable for a mobile app that must come back by itself after any outage.
 */
export class G9ReconnectPolicy implements IRetryPolicy {
  /** The default nominal delays. */
  static readonly DEFAULT_DELAYS_MS = DEFAULT_DELAYS_MS;
  /** The default cap of a nominal delay (30 s). */
  static readonly DEFAULT_MAX_DELAY_MS = 30000;
  /** The default relative jitter (±20%). */
  static readonly DEFAULT_JITTER = 0.2;

  private readonly _delayer: Delayer;

  constructor(options: G9ReconnectPolicyOptions = {}) {
    const delays = [...(options.delaysMs ?? DEFAULT_DELAYS_MS)];
    const maxDelay = options.maxDelayMs ?? G9ReconnectPolicy.DEFAULT_MAX_DELAY_MS;
    const jitter = options.jitter ?? G9ReconnectPolicy.DEFAULT_JITTER;
    const maxAttempts = options.maxAttempts ?? Infinity;
    const maxElapsed = options.maxElapsedMs ?? Infinity;

    if (delays.some((d) => !Number.isFinite(d) || d < 0)) throw new RangeError('delaysMs must hold finite, non-negative numbers.');
    if (!(maxDelay >= 0)) throw new RangeError('maxDelayMs must be non-negative.');
    if (!(jitter >= 0 && jitter < 1)) throw new RangeError('jitter must be in [0, 1).');
    if (!(maxAttempts >= 0)) throw new RangeError('maxAttempts must be non-negative.');
    if (!(maxElapsed >= 0)) throw new RangeError('maxElapsedMs must be non-negative.');

    this._delayer = (context) => {
      const attempt = context.previousRetryCount;
      if (attempt >= maxAttempts) return null;
      if (context.elapsedMilliseconds >= maxElapsed) return null;
      const nominal = attempt < delays.length ? (delays[attempt] ?? maxDelay) : maxDelay;
      return applyJitter(Math.min(nominal, maxDelay), jitter);
    };
  }

  /**
   * The curve of the .NET `G9CClientReconnectPolicy` default constructor: exponential backoff from 200 ms (factor 2,
   * capped at 30 s, ±15% jitter) that gives up after 5 minutes. Pass options to change any of these.
   */
  static exponential(options: G9ExponentialReconnectOptions = {}): G9ReconnectPolicy {
    const baseDelay = options.baseDelayMs ?? 200;
    const factor = options.factor ?? 2;
    const maxDelay = options.maxDelayMs ?? 30000;
    const maxElapsed = options.maxElapsedMs ?? 5 * 60 * 1000;
    const jitter = options.jitter ?? 0.15;

    if (!(baseDelay > 0)) throw new RangeError('baseDelayMs must be positive.');
    if (!(factor >= 1)) throw new RangeError('factor must be at least 1.');
    if (!(maxDelay >= baseDelay)) throw new RangeError('maxDelayMs must be at least baseDelayMs.');
    if (!(jitter >= 0 && jitter < 1)) throw new RangeError('jitter must be in [0, 1).');

    return G9ReconnectPolicy.fromDelegate((context) => {
      if (Number.isFinite(maxElapsed) && context.elapsedMilliseconds >= maxElapsed) return null;
      const raw = baseDelay * Math.pow(factor, context.previousRetryCount);
      return applyJitter(Math.min(raw, maxDelay), jitter);
    });
  }

  /**
   * A policy that delegates every decision to `next` (return the delay in ms, or `null` to give up). Twin of
   * `G9CClientReconnectPolicy.FromDelegate`.
   */
  static fromDelegate(next: (context: RetryContext) => number | null): G9ReconnectPolicy {
    if (typeof next !== 'function') throw new TypeError('A delegate is required.');
    const policy = new G9ReconnectPolicy();
    (policy as unknown as { _delayer: Delayer })._delayer = next;
    return policy;
  }

  /** @inheritdoc SignalR's `IRetryPolicy`: the delay before the next retry, or `null` to stop retrying. */
  nextRetryDelayInMilliseconds(retryContext: RetryContext): number | null {
    return this._delayer(retryContext);
  }

  /**
   * The (jittered) delay before the retry with index `attempt` (0 = the first retry), ignoring elapsed time; `null`
   * when the policy gives up at that attempt.
   */
  delayFor(attempt: number): number | null {
    return this._delayer({
      previousRetryCount: Math.max(0, Math.floor(attempt)),
      elapsedMilliseconds: 0,
      retryReason: new Error('delayFor'),
    });
  }
}

function applyJitter(nominal: number, jitter: number): number {
  if (nominal <= 0 || jitter === 0) return Math.max(0, nominal);
  const factor = 1 - jitter + Math.random() * 2 * jitter;
  return Math.max(0, Math.round(nominal * factor));
}

/** Normalizes the `reconnectPolicy` option of {@link G9Client} into an IRetryPolicy. */
export function toRetryPolicy(policy: IRetryPolicy | G9ReconnectPolicyOptions | undefined): IRetryPolicy {
  if (policy === undefined) return new G9ReconnectPolicy();
  if (typeof (policy as IRetryPolicy).nextRetryDelayInMilliseconds === 'function') return policy as IRetryPolicy;
  return new G9ReconnectPolicy(policy as G9ReconnectPolicyOptions);
}
