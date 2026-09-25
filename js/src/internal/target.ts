import { TransferFormat, type HubConnection, type IHubProtocol } from '@microsoft/signalr';
import type { G9Client } from '../client.js';

/** What the file-transfer classes and the quality monitor accept: a {@link G9Client} or a bare HubConnection. */
export type G9ConnectionTarget = G9Client | HubConnection;

/** Duck-typed G9Client check (works across duplicated copies of this package, unlike instanceof). */
export function isG9Client(target: unknown): target is G9Client {
  if (typeof target !== 'object' || target === null) return false;
  const candidate = target as Partial<G9Client>;
  return (
    typeof candidate.onReconnected === 'function' &&
    typeof candidate.onState === 'function' &&
    typeof candidate.connection === 'object' &&
    candidate.connection !== null
  );
}

/** The HubConnection behind a target. */
export function resolveConnection(target: G9ConnectionTarget): HubConnection {
  if (target == null) throw new TypeError('A G9Client or HubConnection is required.');
  return isG9Client(target) ? target.connection : target;
}

/**
 * The hub protocol a connection uses. HubConnection keeps it in a private field (`_protocol`); a public `protocol`
 * property (test doubles, future SignalR versions) wins when present. Undefined when neither exists.
 */
export function protocolOf(connection: HubConnection): IHubProtocol | undefined {
  const holder = connection as unknown as { protocol?: IHubProtocol; _protocol?: IHubProtocol };
  return holder.protocol ?? holder._protocol;
}

/**
 * True when the connection speaks a binary protocol (MessagePack), where `byte[]` travels as raw bytes. False for
 * JSON (and when the protocol cannot be read, JSON being SignalR's default), where `byte[]` travels as base64 text.
 */
export function isBinaryProtocol(connection: HubConnection): boolean {
  const protocol = protocolOf(connection);
  if (!protocol) return false;
  if (protocol.transferFormat === TransferFormat.Binary) return true;
  return typeof protocol.name === 'string' && protocol.name.toLowerCase() === 'messagepack';
}
