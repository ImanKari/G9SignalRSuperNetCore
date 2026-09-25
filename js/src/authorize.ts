import type { HubConnection } from '@microsoft/signalr';
import { buildHubConnection, type G9ClientOptions, type G9LogLevel } from './client.js';
import { abortErrorFrom, G9TimeoutError } from './errors.js';
import { toError } from './internal/async.js';

/**
 * Result of the JWT authorize route. Twin of the .NET `G9DtAuthorizeResult` (the server DTO of the same name, with
 * System.Text.Json's camelCase names).
 */
export interface G9AuthorizeResult {
  /** Whether the credentials were accepted. */
  isAccepted: boolean;
  /** The issued JWT when accepted. */
  jwToken?: string | null;
  /**
   * Why the credentials were refused. `"G9_RATE_LIMITED"` ({@link G9ErrorCodes}.RateLimited) when the server's 2.9
   * auth throttle refused the attempt without checking the credentials.
   */
  rejectionReason?: string | null;
  /** Extra data the server's authorize handler attached. */
  extraData?: unknown;
}

/** Options of {@link authorize}. */
export interface G9AuthorizeOptions {
  /** Budget for the whole exchange (connect + answer). Default 30000 ms. */
  timeoutMs?: number;
  /** Extra HTTP headers for the auth connection. */
  headers?: Record<string, string>;
  /** Transport restriction for the auth connection. */
  transport?: G9ClientOptions['transport'];
  /** Send cookies/credentials with cross-origin requests. */
  withCredentials?: boolean;
  /** SignalR log level for the auth connection. Default `'warning'`. */
  logLevel?: G9LogLevel;
  /** Cancels the exchange (rejects with an `AbortError`). */
  signal?: AbortSignal;
  /** Replaces building the auth connection (tests). */
  connectionFactory?: (authHubUrl: string) => HubConnection;
}

/** The hub method the client invokes on the auth route (server: `G9GetJwtHub.Authorize(object)`). */
export const AUTHORIZE_METHOD = 'Authorize';
/** The client callback the server answers with (server: `Clients.Caller.SendCoreAsync("AuthorizeResult", ...)`). */
export const AUTHORIZE_RESULT_CALLBACK = 'AuthorizeResult';

function pick(record: Record<string, unknown>, ...names: string[]): unknown {
  for (const name of names) {
    if (name in record) return record[name];
  }
  return undefined;
}

/** Reads the server's `G9DtAuthorizeResult` whatever the protocol's property casing. */
export function normalizeAuthorizeResult(raw: unknown): G9AuthorizeResult {
  const record = (typeof raw === 'object' && raw !== null ? raw : {}) as Record<string, unknown>;
  const jwToken = pick(record, 'jwToken', 'JWToken', 'jWToken', 'jwtoken');
  const rejectionReason = pick(record, 'rejectionReason', 'RejectionReason');
  const result: G9AuthorizeResult = {
    isAccepted: pick(record, 'isAccepted', 'IsAccepted') === true,
    jwToken: typeof jwToken === 'string' ? jwToken : null,
    rejectionReason: typeof rejectionReason === 'string' ? rejectionReason : null,
  };
  const extraData = pick(record, 'extraData', 'ExtraData');
  result.extraData = extraData === undefined ? null : extraData;
  return result;
}

/**
 * Exchanges credentials for a JWT on a G9 JWT authorize route. Twin of the .NET
 * `G9SignalRSuperNetCoreClientWithJWTAuth.AuthorizeAsync`: connects to the auth hub, sends `Authorize(authorizeData)`,
 * waits for the server's `AuthorizeResult` callback and disconnects.
 *
 * A refusal is a result (`isAccepted: false`), not an error; the promise rejects only when the exchange itself fails
 * (connect failure, the connection closing first, timeout → `TimeoutError`, abort → `AbortError`).
 *
 * ```ts
 * const auth = await authorize('https://host/auth/chat', { user, password });
 * if (!auth.isAccepted) throw new Error(auth.rejectionReason ?? 'refused');
 * const client = new G9Client({ url: 'https://host/hubs/chat', accessTokenFactory: () => auth.jwToken! });
 * ```
 */
export async function authorize(
  authHubUrl: string,
  authorizeData: unknown,
  options: G9AuthorizeOptions = {},
): Promise<G9AuthorizeResult> {
  if (!authHubUrl) throw new TypeError('An auth hub URL is required.');
  if (authorizeData === null || authorizeData === undefined) throw new TypeError('authorizeData is required.');
  const signal = options.signal;
  if (signal?.aborted) throw abortErrorFrom(signal);
  const timeoutMs = options.timeoutMs ?? 30000;
  if (!(timeoutMs > 0)) throw new RangeError('timeoutMs must be positive.');

  const connection = options.connectionFactory
    ? options.connectionFactory(authHubUrl)
    : buildHubConnection({
        url: authHubUrl,
        headers: options.headers,
        transport: options.transport,
        withCredentials: options.withCredentials,
        logLevel: options.logLevel,
        statefulReconnect: false,
      });

  let settled = false;
  let resolveResult!: (result: G9AuthorizeResult) => void;
  let rejectResult!: (error: unknown) => void;
  const result = new Promise<G9AuthorizeResult>((resolve, reject) => {
    resolveResult = (value) => {
      if (settled) return;
      settled = true;
      resolve(value);
    };
    rejectResult = (error) => {
      if (settled) return;
      settled = true;
      reject(error);
    };
  });

  connection.on(AUTHORIZE_RESULT_CALLBACK, (raw: unknown) => resolveResult(normalizeAuthorizeResult(raw)));
  connection.onclose((error) =>
    rejectResult(error ?? new Error('The auth connection closed before the AuthorizeResult callback arrived.')),
  );

  const timer = setTimeout(
    () => rejectResult(new G9TimeoutError(`No AuthorizeResult within ${timeoutMs} ms from ${authHubUrl}.`)),
    timeoutMs,
  );
  const onAbort = (): void => rejectResult(abortErrorFrom(signal));
  signal?.addEventListener('abort', onAbort, { once: true });

  try {
    const exchange = (async () => {
      await connection.start();
      await connection.send(AUTHORIZE_METHOD, authorizeData);
    })();
    exchange.catch((error: unknown) => rejectResult(toError(error)));
    return await result;
  } finally {
    clearTimeout(timer);
    signal?.removeEventListener('abort', onAbort);
    try {
      await connection.stop();
    } catch {
      // Best-effort teardown, like the .NET client.
    }
  }
}
