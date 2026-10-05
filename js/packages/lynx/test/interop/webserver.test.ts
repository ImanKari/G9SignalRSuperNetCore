// Interop with the REAL .NET server: starts G9SignalRSuperNetCore.WebServer (ChatHub: JSON + MessagePack, upload
// service, G9Ping) with `dotnet run` and drives it from the Lynx-shaped sandbox over every transport path. Opt-in
// locally (`npm run test:interop`, needs the .NET 10 SDK); CI runs it.
import { spawn, type ChildProcess } from 'node:child_process';
import { mkdtemp, rm } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { bundleForLynx, runInLynxSandbox } from '../../../../scripts/lynx-sandbox.mjs';
import { createG9SignalRBridge } from '../../src/lynxtron/index.js';
import { callbackModuleOf } from '../helpers/native.js';

const here = dirname(fileURLToPath(import.meta.url));
const project = join(here, '..', '..', '..', '..', '..', 'G9SignalRSuperNetCore', 'G9SignalRSuperNetCore.WebServer');

let server: ChildProcess | undefined;
let baseUrl = '';
let dir = '';
let code = '';

function freePort(): Promise<number> {
  return new Promise((resolve, reject) => {
    const probe = createServer();
    probe.once('error', reject);
    probe.listen(0, '127.0.0.1', () => {
      const port = (probe.address() as { port: number }).port;
      probe.close(() => resolve(port));
    });
  });
}

async function waitHealthy(url: string, ms: number): Promise<void> {
  const deadline = Date.now() + ms;
  let last: unknown;
  while (Date.now() < deadline) {
    if (server?.exitCode !== null && server?.exitCode !== undefined) throw new Error(`The server exited (${server.exitCode}).`);
    try {
      const response = await fetch(`${url}/healthz`);
      if (response.ok) return;
    } catch (error) {
      last = error;
    }
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error(`The .NET sample server did not become healthy: ${String(last)}`);
}

beforeAll(async () => {
  const port = await freePort();
  baseUrl = `http://127.0.0.1:${port}`;
  server = spawn('dotnet', ['run', '--project', project, '-c', 'Release', '--no-launch-profile', '--urls', baseUrl], {
    stdio: ['ignore', 'pipe', 'pipe'],
    env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', DOTNET_NOLOGO: '1' },
  });
  let output = '';
  server.stdout?.on('data', (d) => {
    output = (output + d).slice(-20000);
    if (process.env.G9_INTEROP_LOG) process.stdout.write(String(d));
  });
  server.stderr?.on('data', (d) => (output = (output + d).slice(-4000)));
  try {
    await waitHealthy(baseUrl, 240_000);
  } catch (error) {
    throw new Error(`${(error as Error).message}\n${output}`);
  }
  dir = (await mkdtemp(join(tmpdir(), 'g9-interop-'))).replace(/\\/g, '/');
  const client = fileURLToPath(new URL('../../../client/src/index.ts', import.meta.url));
  code = await bundleForLynx(join(here, 'scenario.ts'), { alias: { '@g9tm/signalr-supernetcore-client': client } });
}, 300_000);

afterAll(async () => {
  server?.kill();
  if (dir) await rm(dir, { recursive: true, force: true });
});

describe.each([
  ['json', 'native'],
  ['messagepack', 'native'],
  ['json', 'native-negotiated'],
  ['messagepack', 'native-negotiated'],
  ['json', 'long-polling'],
  ['messagepack', 'long-polling'],
] as const)('the .NET sample hub from a Lynx-shaped runtime (%s, %s)', (protocol, path) => {
  it('connects, invokes, receives, streams both ways, pings and transfers files', async () => {
    const result = (await runInLynxSandbox({
      code,
      fetch,
      host: { url: `${baseUrl}/chat`, dir, protocol, path },
      // The long-polling case keeps the module for FILES; only the client is told to go without it (native: null).
      nativeModules: { G9SignalRLynxModule: callbackModuleOf(createG9SignalRBridge()) },
      timeoutMs: 120_000,
    })) as Record<string, any>;

    expect(result.choice).toBe(path === 'native' ? 'native-websocket' : path === 'native-negotiated' ? 'native-websocket-negotiated' : 'long-polling');
    expect(result.recent).toBe(4);
    expect(result.received).toContain(`lynx:hello over ${path}`);
    expect(result.ticks).toHaveLength(5);
    expect(result.sum).toBe(5050);
    expect(['good', 'fair', 'poor']).toContain(result.quality);
    expect(result.upload).toBe(true);
    expect(typeof result.storedFileName).toBe('string');
    expect(result.download).toBe(true);
  }, 180_000);
});
