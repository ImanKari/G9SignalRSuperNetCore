import type { HubConnection } from '@microsoft/signalr';

type ErrorListener = (error?: Error) => void;
type ReconnectedListener = (connectionId?: string) => void;

interface LifecycleHub {
  readonly reconnecting: Set<ErrorListener>;
  readonly reconnected: Set<ReconnectedListener>;
  readonly closed: Set<ErrorListener>;
}

/** Unsubscribable views of a HubConnection's lifecycle callbacks. */
export interface ConnectionLifecycle {
  onReconnecting(listener: ErrorListener): () => void;
  onReconnected(listener: ReconnectedListener): () => void;
  onClose(listener: ErrorListener): () => void;
}

const registry = new WeakMap<HubConnection, LifecycleHub>();

function fanOut<A extends unknown[]>(listeners: Set<(...args: A) => void>, args: A): void {
  for (const listener of [...listeners]) {
    try {
      listener(...args);
    } catch {
      // One failing listener must not starve the others.
    }
  }
}

/**
 * `HubConnection.onclose/onreconnecting/onreconnected` cannot be unregistered. This registers ONE callback of each
 * kind per connection (the first time it is asked for) and fans out to listeners that can unsubscribe, so monitors
 * and waiters that come and go do not leak callbacks on a long-lived connection.
 */
export function lifecycleOf(connection: HubConnection): ConnectionLifecycle {
  let hub = registry.get(connection);
  if (!hub) {
    const created: LifecycleHub = { reconnecting: new Set(), reconnected: new Set(), closed: new Set() };
    connection.onreconnecting((error) => fanOut(created.reconnecting, [error]));
    connection.onreconnected((connectionId) => fanOut(created.reconnected, [connectionId]));
    connection.onclose((error) => fanOut(created.closed, [error]));
    registry.set(connection, created);
    hub = created;
  }

  const target = hub;
  return {
    onReconnecting(listener) {
      target.reconnecting.add(listener);
      return () => void target.reconnecting.delete(listener);
    },
    onReconnected(listener) {
      target.reconnected.add(listener);
      return () => void target.reconnected.delete(listener);
    },
    onClose(listener) {
      target.closed.add(listener);
      return () => void target.closed.delete(listener);
    },
  };
}
