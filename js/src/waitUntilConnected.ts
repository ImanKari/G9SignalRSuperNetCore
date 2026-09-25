import { HubConnectionState, type HubConnection } from '@microsoft/signalr';
import { abortErrorFrom, G9TimeoutError } from './errors.js';
import { lifecycleOf } from './internal/lifecycle.js';

const POLL_INTERVAL_MS = 100;

/**
 * Completes when `connection` is in the `Connected` state: at once when it already is, otherwise when a reconnect
 * completes or a state poll (every 100 ms at most) sees it connected. Useful before sending after a network change,
 * instead of failing the call while the automatic reconnect is still running. It does NOT start the connection.
 *
 * Twin of the .NET `G9HubConnectionExtensions.WaitUntilConnectedAsync(connection, timeout, cancellationToken)`.
 *
 * @param connection The connection to wait for.
 * @param timeoutMs How long to wait; `Infinity` waits until `signal` aborts. Must be non-negative.
 * @param signal Cancels the wait (rejects with an `AbortError`).
 * @param extraWake Optional extra trigger (used by G9Client to wake on its own restarts); returns an unsubscribe.
 * @throws {G9TimeoutError} The connection was not connected within `timeoutMs` (`error.name === 'TimeoutError'`).
 */
export function waitUntilConnected(
  connection: HubConnection,
  timeoutMs: number,
  signal?: AbortSignal,
  extraWake?: (wake: () => void) => () => void,
): Promise<void> {
  if (connection == null) return Promise.reject(new TypeError('A HubConnection is required.'));
  if (typeof timeoutMs !== 'number' || Number.isNaN(timeoutMs) || timeoutMs < 0) {
    return Promise.reject(new RangeError('The timeout must be non-negative or Infinity.'));
  }
  if (connection.state === HubConnectionState.Connected) return Promise.resolve();
  if (signal?.aborted) return Promise.reject(abortErrorFrom(signal));

  return new Promise<void>((resolve, reject) => {
    let finished = false;
    const unsubscribers: Array<() => void> = [];
    let deadline: ReturnType<typeof setTimeout> | undefined;

    const finish = (error?: unknown): void => {
      if (finished) return;
      finished = true;
      clearInterval(poll);
      if (deadline !== undefined) clearTimeout(deadline);
      signal?.removeEventListener('abort', onAbort);
      for (const unsubscribe of unsubscribers) unsubscribe();
      if (error === undefined) resolve();
      else reject(error);
    };

    const check = (): boolean => {
      if (!finished && connection.state === HubConnectionState.Connected) {
        finish();
        return true;
      }
      return false;
    };

    const onAbort = (): void => finish(abortErrorFrom(signal));

    const poll = setInterval(check, POLL_INTERVAL_MS);
    unsubscribers.push(lifecycleOf(connection).onReconnected(() => void check()));
    if (extraWake) unsubscribers.push(extraWake(() => void check()));
    signal?.addEventListener('abort', onAbort, { once: true });

    if (Number.isFinite(timeoutMs)) {
      deadline = setTimeout(() => {
        if (!check()) {
          finish(new G9TimeoutError(`The hub connection did not reach the Connected state within ${timeoutMs} ms.`));
        }
      }, timeoutMs);
    }
  });
}
