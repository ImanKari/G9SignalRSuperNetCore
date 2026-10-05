import { createServer } from 'node:http';
import { afterEach, describe, expect, it } from 'vitest';
import { WebSocketServer } from 'ws';
import { createLynxWebSocket, G9LynxWebSocket, type G9LynxSocketEvent } from '../src/index.js';
import { natives, until } from './helpers/native.js';

interface Echo {
  url: string;
  received: Array<{ data: Buffer; binary: boolean }>;
  headers: Array<Record<string, unknown>>;
  close(): Promise<void>;
  closeClients(code: number, reason: string): void;
}

async function echoServer(): Promise<Echo> {
  const server = createServer();
  const wss = new WebSocketServer({ server, handleProtocols: (set) => (set.has('g9') ? 'g9' : false) });
  const received: Echo['received'] = [];
  const headers: Echo['headers'] = [];
  wss.on('connection', (socket, request) => {
    headers.push(request.headers as Record<string, unknown>);
    socket.on('message', (data, binary) => {
      received.push({ data: data as Buffer, binary });
      socket.send(data, { binary });
    });
  });
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const port = (server.address() as { port: number }).port;
  return {
    url: `ws://127.0.0.1:${port}/socket`,
    received,
    headers,
    closeClients: (code, reason) => {
      for (const client of wss.clients) client.close(code, reason);
    },
    close: () =>
      new Promise<void>((resolve) => {
        for (const client of wss.clients) client.terminate();
        wss.close(() => server.close(() => resolve()));
      }),
  };
}

function collect(socket: G9LynxWebSocket): G9LynxSocketEvent[] {
  const events: G9LynxSocketEvent[] = [];
  for (const type of ['open', 'message', 'error', 'close']) socket.addEventListener(type, (event) => events.push(event));
  return events;
}

let servers: Echo[] = [];
afterEach(async () => {
  await Promise.all(servers.map((s) => s.close()));
  servers = [];
});

describe.each(natives())('G9LynxWebSocket over the %s', (_name, native) => {
  it('opens with the sub-protocol and headers, then echoes text and binary in order', async () => {
    const server = await echoServer();
    servers.push(server);
    const Socket = createLynxWebSocket({ native, headers: { 'X-G9': 'lynx' } });
    const socket = new Socket(server.url, ['g9']);
    const events = collect(socket);
    expect(socket.readyState).toBe(G9LynxWebSocket.CONNECTING);
    expect(() => socket.send('too early')).toThrow(/CONNECTING/);

    await until(() => socket.readyState === G9LynxWebSocket.OPEN);
    expect(socket.protocol).toBe('g9');
    expect(server.headers[0]!['x-g9']).toBe('lynx');

    socket.send('one');
    socket.send(new Uint8Array([1, 2, 3]).buffer);
    socket.send(new Uint8Array([9, 8, 7, 6, 5]).subarray(1, 3)); // a view's window, not its whole buffer
    socket.send('two');
    await until(() => events.filter((e) => e.type === 'message').length === 4);

    const messages = events.filter((e) => e.type === 'message').map((e) => e.data);
    expect(messages[0]).toBe('one');
    expect(Array.from(new Uint8Array(messages[1] as ArrayBuffer))).toEqual([1, 2, 3]);
    expect(Array.from(new Uint8Array(messages[2] as ArrayBuffer))).toEqual([8, 7]);
    expect(messages[3]).toBe('two');
    expect(server.received.map((r) => r.binary)).toEqual([false, true, true, false]);

    socket.close(1000, 'bye');
    await until(() => socket.readyState === G9LynxWebSocket.CLOSED);
    const close = events.find((e) => e.type === 'close')!;
    expect(close).toMatchObject({ code: 1000, wasClean: true });
    expect(events.map((e) => e.type)[0]).toBe('open');
  });

  it('reports a server close with its code and reason, once', async () => {
    const server = await echoServer();
    servers.push(server);
    const Socket = createLynxWebSocket({ native });
    const socket = new Socket(server.url);
    const events = collect(socket);
    let onclose = 0;
    socket.onclose = () => onclose++;
    await until(() => socket.readyState === 1);
    server.closeClients(4001, 'kicked');
    await until(() => socket.readyState === 3);
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(events.filter((e) => e.type === 'close')).toEqual([expect.objectContaining({ code: 4001, reason: 'kicked' })]);
    expect(onclose).toBe(1);
    socket.send('after close is dropped');
  });

  it('fails to connect with error then close (1006), never open', async () => {
    const Socket = createLynxWebSocket({ native });
    const socket = new Socket('ws://127.0.0.1:1/nothing-listens-here');
    const events = collect(socket);
    await until(() => socket.readyState === 3, 10000);
    expect(events.map((e) => e.type)).not.toContain('open');
    expect(events.at(-1)).toMatchObject({ type: 'close', code: 1006, wasClean: false });
  });

  it('refuses binaryType blob and invalid close codes like a browser', async () => {
    const server = await echoServer();
    servers.push(server);
    const Socket = createLynxWebSocket({ native });
    const socket = new Socket(server.url);
    expect(() => {
      socket.binaryType = 'blob';
    }).toThrow(/arraybuffer/);
    socket.binaryType = 'arraybuffer';
    await until(() => socket.readyState === 1);
    expect(() => socket.close(1001)).toThrow(/3000-4999/);
    socket.close();
  });
});

describe('without a native module', () => {
  it('says how to link it', () => {
    expect(() => new G9LynxWebSocket('ws://x')).toThrow(/Autolink/);
  });
});
