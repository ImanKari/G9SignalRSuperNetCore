// The Lynx package end to end in a Lynx-shaped runtime (scripts/lynx-sandbox.mjs): no WebSocket, URL, TextEncoder or
// BigInt in the sandbox; the native module is the contract's Node implementation behind a copying bridge.
import { createHash } from 'node:crypto';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { bundleForLynx, runInLynxSandbox } from '../../../../scripts/lynx-sandbox.mjs';
import { createG9SignalRBridge } from '../../src/lynxtron/index.js';
import { callbackModuleOf } from '../helpers/native.js';
import { MiniHub } from '../helpers/miniHub.js';

const here = dirname(fileURLToPath(import.meta.url));
let hub: MiniHub;
let url: string;
let dir: string;

function bytesOf(value: unknown): Buffer {
  if (typeof value === 'string') return Buffer.from(value, 'base64');
  return Buffer.from(value as Uint8Array);
}

beforeAll(async () => {
  hub = new MiniHub();
  url = await hub.start();
  dir = (await mkdtemp(join(tmpdir(), 'g9-lynxsim-'))).replace(/\\/g, '/');
  hub.methods.set('EchoBytes', ([value]) => value);
  hub.methods.set('Count', ([n]) =>
    (async function* () {
      for (let i = 0; i < (n as number); i++) yield i;
    })(),
  );
  hub.methods.set('Shout', ([text], connection) => connection.send('Heard', String(text).toUpperCase()));
  const files = new Map<string, Buffer>();
  hub.methods.set('BeginUpload', ([uploadId, , , chunkSize]) => ({ uploadId, bytesAlreadyReceived: 0, chunkSize, alreadyCompleted: false }));
  hub.methods.set('UploadChunks', async ([uploadId, chunks]) => {
    let stored = Buffer.alloc(0);
    for await (const chunk of chunks as AsyncIterable<unknown>) stored = Buffer.concat([stored, bytesOf(chunk)]);
    const name = `u/${String(uploadId).slice(0, 8)}.bin`;
    files.set(name, stored);
    return { status: 0, bytesWritten: stored.length, sha256: createHash('sha256').update(stored).digest('hex'), storedFileName: name };
  });
  hub.methods.set('BeginDownload', ([name, resumeFrom, chunkSize]) => {
    const file = files.get(String(name))!;
    return { notFound: !file, totalBytes: file.length, sha256: createHash('sha256').update(file).digest('hex'), chunkSize, resumeFrom };
  });
  hub.methods.set('DownloadChunks', ([name, resumeFrom, chunkSize], connection) => {
    const file = files.get(String(name))!;
    const binary = connection.protocol.name === 'messagepack';
    return (async function* () {
      for (let at = resumeFrom as number; at < file.length; at += chunkSize as number) {
        const slice = file.subarray(at, Math.min(file.length, at + (chunkSize as number)));
        yield binary ? new Uint8Array(slice) : slice.toString('base64');
      }
    })();
  });
});

afterAll(async () => {
  await hub.stop();
  await rm(dir, { recursive: true, force: true });
});

describe('the Lynx package in a Lynx-shaped runtime', () => {
  it('connects over the native WebSocket, transfers files, streams and receives pushes (JSON + MessagePack)', async () => {
    const client = fileURLToPath(new URL('../../../client/src/index.ts', import.meta.url));
    const code = await bundleForLynx(join(here, 'scenario.ts'), { alias: { '@g9tm/signalr-supernetcore-client': client } });
    const result = (await runInLynxSandbox({
      code,
      fetch,
      host: { url, dir },
      nativeModules: { G9SignalRLynxModule: callbackModuleOf(createG9SignalRBridge()) },
    })) as Record<string, any>;

    expect(result.env).toEqual({ URL: 'undefined', WebSocket: 'undefined', TextEncoder: 'undefined', BigInt: 'undefined' });
    for (const name of ['json', 'messagepack']) {
      expect(result[name], name).toMatchObject({
        choice: 'native-websocket',
        phase: 'connected',
        items: [0, 1, 2, 3],
        heard: ['SALAM'],
        upload: true,
        download: true,
      });
    }
    expect(result.messagepack.echoedKind).toBe('[object Uint8Array]');
    expect(result.messagepack.echoedLength).toBe(50_000);
    // Lynx is not Node, so SignalR put the token in the query string.
    expect(hub.requests.every((r) => r.url === '/hub?access_token=tok%20en')).toBe(true);
  });
});
