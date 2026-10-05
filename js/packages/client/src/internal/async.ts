import { abortErrorFrom } from '../errors.js';

/** Throws the abort error of `signal` when it is aborted. */
export function throwIfAborted(signal?: AbortSignal): void {
  if (signal?.aborted) throw abortErrorFrom(signal);
}

/** Resolves after `ms` milliseconds; rejects early with the abort error when `signal` aborts. */
export function delay(ms: number, signal?: AbortSignal): Promise<void> {
  if (signal?.aborted) return Promise.reject(abortErrorFrom(signal));
  return new Promise<void>((resolve, reject) => {
    const onAbort = (): void => {
      clearTimeout(timer);
      reject(abortErrorFrom(signal));
    };
    const timer = setTimeout(() => {
      signal?.removeEventListener('abort', onAbort);
      resolve();
    }, Math.max(0, ms));
    signal?.addEventListener('abort', onAbort, { once: true });
  });
}

/**
 * Settles like `promise`, or rejects with the abort error as soon as `signal` aborts. The original promise keeps
 * running (SignalR invocations cannot be cancelled); its late rejection is swallowed so it is never unhandled.
 */
export function raceAbort<T>(promise: Promise<T>, signal?: AbortSignal): Promise<T> {
  if (!signal) return promise;
  if (signal.aborted) {
    promise.catch(() => undefined);
    return Promise.reject(abortErrorFrom(signal));
  }
  return new Promise<T>((resolve, reject) => {
    const onAbort = (): void => {
      promise.catch(() => undefined);
      reject(abortErrorFrom(signal));
    };
    signal.addEventListener('abort', onAbort, { once: true });
    promise.then(
      (value) => {
        signal.removeEventListener('abort', onAbort);
        resolve(value);
      },
      (error: unknown) => {
        signal.removeEventListener('abort', onAbort);
        reject(error);
      },
    );
  });
}

/** Gives the event loop a turn (a macrotask), so long CPU work does not freeze a UI thread. */
export function yieldToEventLoop(): Promise<void> {
  return new Promise<void>((resolve) => setTimeout(resolve, 0));
}

/** Monotonic milliseconds (performance.now where available). */
export function monotonicNow(): number {
  return typeof performance !== 'undefined' && typeof performance.now === 'function' ? performance.now() : Date.now();
}

/** A human readable summary of an unknown error value. */
export function describeError(error: unknown): string {
  if (error instanceof Error) return error.name && error.name !== 'Error' ? `${error.name}: ${error.message}` : error.message;
  if (typeof error === 'string') return error;
  try {
    return JSON.stringify(error) ?? String(error);
  } catch {
    return String(error);
  }
}

/** Converts an unknown thrown value into an Error. */
export function toError(error: unknown): Error {
  return error instanceof Error ? error : new Error(describeError(error));
}

/** Calls a user callback and swallows what it throws: a subscriber must not break the library's own flow. */
export function safeInvoke<A extends unknown[]>(callback: ((...args: A) => void) | undefined, ...args: A): void {
  if (!callback) return;
  try {
    callback(...args);
  } catch {
    // A subscriber failure must not stop the caller.
  }
}
