// A small SignalR hub server over `ws` for end-to-end tests of the Lynx WebSocket path without .NET: the handshake,
// invocations (with and without completion), server-to-client streams, server pushes, pings, abrupt drops. Speaks
// JSON and MessagePack with the protocol classes of @microsoft/signalr, so the bytes are what a real hub sends.
import { JsonHubProtocol, MessageType, type HubMessage, type IHubProtocol } from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import { decode } from '@msgpack/msgpack';
import { createServer, type IncomingMessage, type Server } from 'node:http';
import { WebSocketServer, type WebSocket } from 'ws';

const RS = '\u001e';

/**
 * Reads client-to-server messages the way a SERVER does. The JS protocol classes are client-side parsers: they ignore
 * StreamInvocation (type 4) and drop an Invocation's `streamIds`, which only a server ever receives.
 */
function parseServerSide(input: string | ArrayBuffer): HubMessage[] {
  if (typeof input === 'string') {
    return input
      .split(RS)
      .filter((part) => part.length > 0)
      .map((part) => JSON.parse(part) as HubMessage);
  }
  const bytes = new Uint8Array(input);
  const out: HubMessage[] = [];
  let offset = 0;
  while (offset < bytes.length) {
    let size = 0;
    let shift = 0;
    let byte: number;
    do {
      byte = bytes[offset++]!;
      size |= (byte & 0x7f) << shift;
      shift += 7;
    } while (byte & 0x80);
    const frame = decode(bytes.subarray(offset, offset + size)) as unknown[];
    offset += size;
    const type = frame[0] as number;
    switch (type) {
      case MessageType.Invocation:
      case MessageType.StreamInvocation:
        out.push({
          type,
          headers: frame[1],
          invocationId: (frame[2] as string | null) ?? undefined,
          target: frame[3],
          arguments: frame[4],
          streamIds: (frame[5] as string[] | undefined) ?? [],
        } as unknown as HubMessage);
        break;
      case MessageType.StreamItem:
        out.push({ type, headers: frame[1], invocationId: frame[2], item: frame[3] } as unknown as HubMessage);
        break;
      case MessageType.Completion: {
        const kind = frame[3] as number;
        out.push({
          type,
          headers: frame[1],
          invocationId: frame[2],
          error: kind === 1 ? (frame[4] as string) : undefined,
          result: kind === 3 ? frame[4] : undefined,
        } as unknown as HubMessage);
        break;
      }
      default:
        out.push({ type } as HubMessage);
    }
  }
  return out;
}

export type HubHandler = (args: unknown[], connection: MiniHubConnection) => unknown;

export interface MiniHubConnection {
  readonly socket: WebSocket;
  readonly protocol: IHubProtocol;
  readonly request: IncomingMessage;
  send(method: string, ...args: unknown[]): void;
}

/** A client-to-server stream the hub reads (an argument of a method invoked with `streamIds`). */
class ClientStream implements AsyncIterable<unknown> {
  private readonly items: unknown[] = [];
  private done = false;
  private error: Error | null = null;
  private wake: (() => void) | null = null;

  push(item: unknown): void {
    this.items.push(item);
    this.notify();
  }

  complete(error?: string): void {
    this.done = true;
    if (error) this.error = new Error(error);
    this.notify();
  }

  private notify(): void {
    const wake = this.wake;
    this.wake = null;
    wake?.();
  }

  async *[Symbol.asyncIterator](): AsyncIterator<unknown> {
    for (;;) {
      if (this.items.length > 0) {
        yield this.items.shift();
        continue;
      }
      if (this.error) throw this.error;
      if (this.done) return;
      await new Promise<void>((resolve) => {
        this.wake = resolve;
      });
    }
  }
}

export class MiniHub {
  readonly methods = new Map<string, HubHandler>();
  private readonly clientStreams = new Map<string, ClientStream>();
  readonly connections = new Set<MiniHubConnection>();
  readonly requests: IncomingMessage[] = [];
  readonly invocations: Array<{ method: string; args: unknown[] }> = [];
  private server: Server | null = null;
  private wss: WebSocketServer | null = null;
  port = 0;

  async start(): Promise<string> {
    this.server = createServer((_req, res) => {
      res.statusCode = 404;
      res.end();
    });
    this.wss = new WebSocketServer({ server: this.server });
    this.wss.on('connection', (socket, request) => this.accept(socket, request));
    await new Promise<void>((resolve) => this.server!.listen(0, '127.0.0.1', resolve));
    this.port = (this.server.address() as { port: number }).port;
    return `http://127.0.0.1:${this.port}/hub`;
  }

  async stop(): Promise<void> {
    for (const connection of this.connections) connection.socket.terminate();
    await new Promise<void>((resolve) => this.wss?.close(() => resolve()));
    await new Promise<void>((resolve) => this.server?.close(() => resolve()));
  }

  /** Drops every connection without a close frame (a network loss as the client sees it). */
  dropAll(): void {
    for (const connection of this.connections) connection.socket.terminate();
  }

  private accept(socket: WebSocket, request: IncomingMessage): void {
    this.requests.push(request);
    let protocol: IHubProtocol | null = null;
    const connection: MiniHubConnection = {
      socket,
      request,
      get protocol() {
        return protocol!;
      },
      send: (method, ...args) => this.write(socket, protocol!, { type: MessageType.Invocation, target: method, arguments: args } as HubMessage),
    };

    socket.on('message', (data, isBinary) => {
      try {
        if (!protocol) {
          const text = Buffer.from(data as Buffer).toString('utf8');
          const end = text.indexOf(RS);
          const handshake = JSON.parse(text.slice(0, end)) as { protocol: string };
          protocol = handshake.protocol === 'messagepack' ? new MessagePackHubProtocol() : new JsonHubProtocol();
          socket.send(protocol.name === 'messagepack' ? Buffer.from('{}' + RS) : '{}' + RS, { binary: protocol.name === 'messagepack' });
          this.connections.add(connection);
          socket.on('close', () => this.connections.delete(connection));
          return;
        }
        const input = isBinary
          ? new Uint8Array(data as Buffer).slice().buffer
          : Buffer.from(data as Buffer).toString('utf8');
        for (const message of parseServerSide(input)) this.handle(connection, message);
      } catch (error) {
        socket.close(1011, String(error));
      }
    });
  }

  private write(socket: WebSocket, protocol: IHubProtocol, message: HubMessage): void {
    const payload = protocol.writeMessage(message);
    if (typeof payload === 'string') socket.send(payload);
    else socket.send(new Uint8Array(payload as ArrayBuffer), { binary: true });
  }

  private handle(connection: MiniHubConnection, message: HubMessage): void {
    const protocol = connection.protocol;
    switch (message.type) {
      case MessageType.Invocation: {
        const { target, invocationId } = message;
        const args: unknown[] = [...message.arguments];
        for (const streamId of message.streamIds ?? []) {
          const stream = new ClientStream();
          this.clientStreams.set(streamId, stream);
          args.push(stream);
        }
        this.invocations.push({ method: target, args });
        const handler = this.methods.get(target);
        Promise.resolve()
          .then(() => {
            if (!handler) throw new Error(`Unknown hub method '${target}'.`);
            return handler(args, connection);
          })
          .then(
            (result) => {
              if (invocationId) this.write(connection.socket, protocol, { type: MessageType.Completion, invocationId, result } as HubMessage);
            },
            (error: unknown) => {
              if (invocationId) {
                this.write(connection.socket, protocol, {
                  type: MessageType.Completion,
                  invocationId,
                  error: `An unexpected error occurred invoking '${target}' on the server. HubException: ${(error as Error).message}`,
                } as HubMessage);
              }
            },
          );
        break;
      }
      case MessageType.StreamInvocation: {
        const { target, arguments: args, invocationId } = message;
        this.invocations.push({ method: target, args });
        const handler = this.methods.get(target);
        void (async () => {
          try {
            if (!handler) throw new Error(`Unknown hub method '${target}'.`);
            const items = (await handler(args, connection)) as AsyncIterable<unknown> | Iterable<unknown>;
            for await (const item of items as AsyncIterable<unknown>) {
              this.write(connection.socket, protocol, { type: MessageType.StreamItem, invocationId, item } as HubMessage);
            }
            this.write(connection.socket, protocol, { type: MessageType.Completion, invocationId } as HubMessage);
          } catch (error) {
            this.write(connection.socket, protocol, {
              type: MessageType.Completion,
              invocationId,
              error: (error as Error).message,
            } as HubMessage);
          }
        })();
        break;
      }
      case MessageType.StreamItem:
        this.clientStreams.get(message.invocationId!)?.push(message.item);
        break;
      case MessageType.Completion: {
        const stream = this.clientStreams.get(message.invocationId);
        if (stream) {
          this.clientStreams.delete(message.invocationId);
          stream.complete(message.error);
        }
        break;
      }
      case MessageType.Ping:
        this.write(connection.socket, protocol, { type: MessageType.Ping } as HubMessage);
        break;
      default:
        break;
    }
  }
}
