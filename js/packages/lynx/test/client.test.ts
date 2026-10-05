// End to end without .NET: G9Client from createLynxClient -> G9LynxWebSocket -> the native module contract (the
// Lynxtron bridge in the Android/iOS callback shape) -> a real WebSocket -> a SignalR hub (JSON and MessagePack).
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import { createHash } from 'node:crypto';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { G9FileDownloader, G9FileUploader, G9UploadStatus } from '@g9tm/signalr-supernetcore-client';
import { createLynxClient, lynxClientOptions, lynxFileSystem, type G9LynxTransportChoice } from '../src/index.js';
import { createG9SignalRBridge } from '../src/lynxtron/index.js';
import { nativeFromCallbackModule } from '../src/native.js';
import { callbackModuleOf, until } from './helpers/native.js';
import { MiniHub } from './helpers/miniHub.js';

const native = () => nativeFromCallbackModule(callbackModuleOf(createG9SignalRBridge()));

function bytesOf(value: unknown): Uint8Array {
  if (typeof value === 'string') return new Uint8Array(Buffer.from(value, 'base64'));
  if (value instanceof Uint8Array) return value;
  return new Uint8Array(value as ArrayBuffer);
}

let hub: MiniHub;
let url: string;
let dir: string;

beforeEach(async () => {
  hub = new MiniHub();
  url = await hub.start();
  dir = await mkdtemp(join(tmpdir(), 'g9-lynx-'));
});

afterEach(async () => {
  await hub.stop();
  await rm(dir, { recursive: true, force: true });
});

describe.each([
  ['json', undefined],
  ['messagepack', () => new MessagePackHubProtocol()],
] as const)('createLynxClient over the native WebSocket (%s)', (name, protocol) => {
  it('connects without negotiating, invokes, streams, receives pushes and sends the access token', async () => {
    hub.methods.set('Echo', ([value]) => value);
    hub.methods.set('Fail', () => {
      throw new Error('G9_RATE_LIMITED');
    });
    hub.methods.set('Count', ([n]) =>
      (async function* () {
        for (let i = 0; i < (n as number); i++) yield i;
      })(),
    );
    hub.methods.set('Shout', ([text], connection) => {
      connection.send('Heard', String(text).toUpperCase());
    });

    let choice: G9LynxTransportChoice | undefined;
    const client = createLynxClient({
      url,
      native: native(),
      protocol: protocol?.(),
      accessTokenFactory: () => 'tok en',
      headers: { 'X-App': 'agripad' },
      onTransportChosen: (c) => (choice = c),
    });
    expect(choice).toBe('native-websocket');
    const heard: string[] = [];
    client.on('Heard', (text: string) => heard.push(text));

    await client.start();
    expect(client.state.phase).toBe('connected');
    const request = hub.requests[0]!;
    // No /negotiate before it. SignalR sends the token as a header where it believes it runs on Node (this test) and
    // as ?access_token= elsewhere (Lynx: see the sandbox test); the native socket carries either.
    expect(request.url?.startsWith('/hub')).toBe(true);
    expect(request.url?.includes('access_token=tok%20en') || request.headers.authorization === 'Bearer tok en').toBe(true);
    expect(request.headers['x-app']).toBe('agripad');

    expect(await client.invoke('Echo', 'سلام')).toBe('سلام');
    const bytes = new Uint8Array(70_000).map((_, i) => (i * 7) & 0xff);
    const echoed = await client.invoke('Echo', name === 'messagepack' ? bytes : Buffer.from(bytes).toString('base64'));
    expect(Buffer.from(bytesOf(echoed)).equals(Buffer.from(bytes))).toBe(true);
    await expect(client.invoke('Fail')).rejects.toThrow(/G9_RATE_LIMITED/);

    const items: number[] = [];
    await new Promise<void>((resolve, reject) =>
      client.stream<number>('Count', 5).subscribe({ next: (i) => items.push(i), error: reject, complete: resolve }),
    );
    expect(items).toEqual([0, 1, 2, 3, 4]);

    await client.send('Shout', 'hi');
    await until(() => heard.length === 1);
    expect(heard).toEqual(['HI']);

    await client.stop();
    expect(client.state.phase).toBe('disconnected');
  });

  it('comes back by itself after the server drops the connection', async () => {
    hub.methods.set('Echo', ([value]) => value);
    const client = createLynxClient({
      url,
      native: native(),
      protocol: protocol?.(),
      reconnectPolicy: { delaysMs: [0, 50, 50], jitter: 0 },
    });
    let reconnects = 0;
    client.onReconnected(() => reconnects++);
    await client.start();
    hub.dropAll();
    await until(() => reconnects === 1, 10000);
    expect(await client.invoke('Echo', 2)).toBe(2);
    await client.stop();
  });

  it('uploads and downloads files through the native file system (the .NET twin path)', async () => {
    const content = new Uint8Array(300 * 1024 + 9).map((_, i) => (i * 31 + 7) & 0xff);
    const source = join(dir, 'in', 'photo.jpg');
    await writeFile(join(dir, 'tmp.bin'), content);
    const fs = lynxFileSystem(native());
    await fs.write(source, 0, content);
    expect((await fs.stat(source))!.size).toBe(content.length);

    let stored = Buffer.alloc(0);
    hub.methods.set('BeginUpload', ([uploadId, , , chunkSize]) => ({
      uploadId,
      bytesAlreadyReceived: 0,
      chunkSize,
      alreadyCompleted: false,
    }));
    hub.methods.set('UploadChunks', async ([, chunks]) => {
      for await (const chunk of chunks as AsyncIterable<unknown>) stored = Buffer.concat([stored, bytesOf(chunk)]);
      return { status: 0, bytesWritten: stored.length, sha256: createHash('sha256').update(stored).digest('hex'), storedFileName: 'u/photo.jpg' };
    });
    hub.methods.set('BeginDownload', ([, resumeFrom, chunkSize]) => ({
      notFound: false,
      totalBytes: stored.length,
      sha256: createHash('sha256').update(stored).digest('hex'),
      chunkSize,
      resumeFrom,
    }));
    hub.methods.set('DownloadChunks', ([, resumeFrom, chunkSize]) =>
      (async function* () {
        for (let at = resumeFrom as number; at < stored.length; at += chunkSize as number) {
          const slice = stored.subarray(at, Math.min(stored.length, at + (chunkSize as number)));
          yield name === 'messagepack' ? new Uint8Array(slice) : slice.toString('base64');
        }
      })(),
    );

    const client = createLynxClient({ url, native: native(), protocol: protocol?.() });
    await client.start();
    const upload = await new G9FileUploader(client).uploadFile(source, { fileSystem: fs });
    expect(upload.status).toBe(G9UploadStatus.Completed);
    expect(Buffer.from(stored).equals(Buffer.from(content))).toBe(true);

    const target = join(dir, 'out', 'photo.jpg');
    const download = await new G9FileDownloader(client).downloadToFile('u/photo.jpg', target, { fileSystem: fs });
    expect(download).toMatchObject({ status: G9UploadStatus.Completed, localPath: target });
    expect(Buffer.from(await readFile(target)).equals(Buffer.from(content))).toBe(true);
    await client.stop();
  });
});

describe('lynxClientOptions', () => {
  it('uses the native WebSocket with negotiation when asked (URL shim installed)', () => {
    let choice: G9LynxTransportChoice | undefined;
    const options = lynxClientOptions({ url, native: native(), skipNegotiation: false, onTransportChosen: (c) => (choice = c) });
    expect(choice).toBe('native-websocket-negotiated');
    expect(options.transport).toBe('websockets');
    expect(options.skipNegotiation).toBe(false);
  });

  it("falls back to the platform's WebSocket without the native module", () => {
    let choice: G9LynxTransportChoice | undefined;
    const options = lynxClientOptions({ url, native: null, onTransportChosen: (c) => (choice = c) });
    expect(choice).toBe('platform'); // Node has a global WebSocket
    expect(options.webSocket).toBeUndefined();
  });
});
