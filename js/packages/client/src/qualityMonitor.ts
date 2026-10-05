import { HubConnectionState, type HubConnection } from '@microsoft/signalr';
import { monotonicNow, safeInvoke } from './internal/async.js';
import { lifecycleOf } from './internal/lifecycle.js';
import { isG9Client, resolveConnection, type G9ConnectionTarget } from './internal/target.js';

/** How usable the connection is. Twin of `G9EConnectionQualityLevel` (Good, Fair, Poor, Lost). */
export type G9ConnectionQualityLevel = 'good' | 'fair' | 'poor' | 'lost';

/** One measurement. Twin of `G9DtConnectionQuality`. */
export interface G9ConnectionQuality {
  /** Measured round trip in ms, or -1 when there is no measurement (failed probe, not connected). */
  rttMs: number;
  /** The level derived from the measurement. */
  level: G9ConnectionQualityLevel;
  /** When it was measured (epoch ms); 0 before the first probe. */
  measuredAt: number;
}

/** Options of {@link G9ConnectionQualityMonitor}. */
export interface G9ConnectionQualityMonitorOptions {
  /** Time between probes. Default 5000 ms. Keep it at 500 ms or more: the server rate-limits G9Ping (2/s, burst 5). */
  intervalMs?: number;
  /** Hub method to probe; it must take a long and return it. Default `'G9Ping'`. */
  pingMethod?: string;
}

/**
 * Measures the round-trip time of the connection at a fixed interval by invoking the hub method `G9Ping(long)` (built
 * into every hub deriving from the G9 hub bases since 2.9) and classifies it, so a UI can show a connection
 * indicator. Twin of the .NET `G9CConnectionQualityMonitor`.
 *
 * Thresholds: good < 150 ms, fair < 400 ms, poor ≥ 400 ms or after one failed probe, lost after 3 failed probes in a
 * row or whenever the connection is not connected (a reconnecting/close event is reported as lost at once, a
 * reconnect triggers a probe right away). A probe that does not answer within the interval (clamped to 1–10 s)
 * counts as failed. {@link onChange} fires only when the level changes; {@link current} always holds the latest
 * measurement.
 */
export class G9ConnectionQualityMonitor {
  /** The hub method probed by default. */
  static readonly DEFAULT_PING_METHOD = 'G9Ping';
  /** Round trips below this are good. */
  static readonly GOOD_BELOW_MS = 150;
  /** Round trips below this (and not good) are fair. */
  static readonly FAIR_BELOW_MS = 400;
  /** Consecutive failed probes after which the level is lost. */
  static readonly LOST_AFTER_FAILURES = 3;
  /** Default probe interval. */
  static readonly DEFAULT_INTERVAL_MS = 5000;

  private static readonly MIN_PROBE_TIMEOUT_MS = 1000;
  private static readonly MAX_PROBE_TIMEOUT_MS = 10000;

  private readonly _target: G9ConnectionTarget;
  private readonly _connection: HubConnection;
  private readonly _intervalMs: number;
  private readonly _probeTimeoutMs: number;
  private readonly _pingMethod: string;
  private readonly _listeners = new Set<(quality: G9ConnectionQuality) => void>();
  private _current: G9ConnectionQuality;
  private _failures = 0;
  private _running = false;
  private _generation = 0;
  private _timer: ReturnType<typeof setTimeout> | undefined;
  private _wake: (() => void) | null = null;
  private _wakeRequested = false;
  private _unsubscribers: Array<() => void> = [];

  constructor(target: G9ConnectionTarget, options: G9ConnectionQualityMonitorOptions = {}) {
    this._target = target;
    this._connection = resolveConnection(target);
    const interval = options.intervalMs ?? G9ConnectionQualityMonitor.DEFAULT_INTERVAL_MS;
    if (!(interval > 0) || !Number.isFinite(interval)) throw new RangeError('intervalMs must be positive.');
    const pingMethod = options.pingMethod ?? G9ConnectionQualityMonitor.DEFAULT_PING_METHOD;
    if (!pingMethod) throw new TypeError('A ping method name is required.');

    this._intervalMs = interval;
    this._probeTimeoutMs = Math.min(
      G9ConnectionQualityMonitor.MAX_PROBE_TIMEOUT_MS,
      Math.max(G9ConnectionQualityMonitor.MIN_PROBE_TIMEOUT_MS, interval),
    );
    this._pingMethod = pingMethod;
    this._current = Object.freeze({
      rttMs: -1,
      level: this._connection.state === HubConnectionState.Connected ? 'good' : 'lost',
      measuredAt: 0,
    });
  }

  /** Classifies a round trip: good < 150 ms, fair < 400 ms, else poor. */
  static classify(rttMs: number): G9ConnectionQualityLevel {
    if (rttMs < G9ConnectionQualityMonitor.GOOD_BELOW_MS) return 'good';
    if (rttMs < G9ConnectionQualityMonitor.FAIR_BELOW_MS) return 'fair';
    return 'poor';
  }

  /** The latest measurement. */
  get current(): G9ConnectionQuality {
    return this._current;
  }

  /** True between {@link start} and {@link stop}. */
  get running(): boolean {
    return this._running;
  }

  /** Called when the level changes (not on every probe). Returns the unsubscribe function. */
  onChange(callback: (quality: G9ConnectionQuality) => void): () => void {
    this._listeners.add(callback);
    return () => void this._listeners.delete(callback);
  }

  /** Starts probing (the first probe runs at once). Calling it while running does nothing. */
  start(): void {
    if (this._running) return;
    this._running = true;
    const generation = ++this._generation;

    const lifecycle = lifecycleOf(this._connection);
    const lost = (): void => {
      if (!this._running) return;
      this._failures = 0;
      this._report('lost', -1);
    };
    this._unsubscribers.push(lifecycle.onReconnecting(lost));
    this._unsubscribers.push(lifecycle.onClose(lost));
    this._unsubscribers.push(lifecycle.onReconnected(() => this._requestProbe()));
    if (isG9Client(this._target)) {
      // A G9Client restart after a final close is a fresh start(), not a SignalR reconnect: probe it at once too.
      this._unsubscribers.push(this._target.onReconnected(() => this._requestProbe()));
    }

    void this._run(generation);
  }

  /** Stops probing and detaches from the connection's events. The connection itself is left alone. */
  stop(): void {
    if (!this._running) return;
    this._running = false;
    this._generation++;
    this._wakeRequested = false;
    for (const unsubscribe of this._unsubscribers) unsubscribe();
    this._unsubscribers = [];
    if (this._timer !== undefined) clearTimeout(this._timer);
    this._timer = undefined;
    const resume = this._wake;
    this._wake = null;
    resume?.();
  }

  private _requestProbe(): void {
    if (!this._running) return;
    const resume = this._wake;
    if (resume) {
      this._wake = null;
      if (this._timer !== undefined) clearTimeout(this._timer);
      this._timer = undefined;
      resume();
    } else {
      this._wakeRequested = true; // a probe is running; run the next one without waiting a full interval
    }
  }

  private async _run(generation: number): Promise<void> {
    while (this._isCurrent(generation)) {
      await this._probe(generation);
      if (!this._isCurrent(generation)) return;
      if (this._wakeRequested) {
        this._wakeRequested = false;
        continue;
      }
      await new Promise<void>((resolve) => {
        this._wake = resolve;
        this._timer = setTimeout(() => {
          this._wake = null;
          this._timer = undefined;
          resolve();
        }, this._intervalMs);
      });
    }
  }

  private _isCurrent(generation: number): boolean {
    return this._running && this._generation === generation;
  }

  private async _probe(generation: number): Promise<void> {
    const connection = this._connection;
    if (connection.state !== HubConnectionState.Connected) {
      this._failures = 0;
      this._report('lost', -1);
      return;
    }

    const started = monotonicNow();
    // The server echoes a long: send an integer (a fractional number would fail its deserialization).
    const timestamp = Math.floor(
      (typeof performance !== 'undefined' && typeof performance.timeOrigin === 'number' ? performance.timeOrigin : 0) +
        started,
    );
    let timeout: ReturnType<typeof setTimeout> | undefined;
    try {
      const call = connection.invoke<unknown>(this._pingMethod, timestamp);
      call.catch(() => undefined);
      await Promise.race([
        call,
        new Promise<never>((_, reject) => {
          timeout = setTimeout(() => reject(new Error('G9Ping timed out.')), this._probeTimeoutMs);
        }),
      ]);
      if (!this._isCurrent(generation)) return;
      const rttMs = monotonicNow() - started;
      this._failures = 0;
      this._report(G9ConnectionQualityMonitor.classify(rttMs), rttMs);
    } catch {
      if (!this._isCurrent(generation)) return; // stopping, not failing
      this._failures++;
      const lost =
        this._failures >= G9ConnectionQualityMonitor.LOST_AFTER_FAILURES ||
        connection.state !== HubConnectionState.Connected;
      this._report(lost ? 'lost' : 'poor', -1);
    } finally {
      if (timeout !== undefined) clearTimeout(timeout);
    }
  }

  private _report(level: G9ConnectionQualityLevel, rttMs: number): void {
    if (!this._running) return;
    const quality: G9ConnectionQuality = Object.freeze({ rttMs, level, measuredAt: Date.now() });
    const changed = quality.level !== this._current.level;
    this._current = quality;
    if (!changed) return;
    for (const listener of [...this._listeners]) safeInvoke(listener, quality);
  }
}
