// Runs scenario.ts in the Lynx-shaped sandbox and checks what it saw and produced.
import { createHash } from 'node:crypto';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { bundleForLynx, runInLynxSandbox } from '../../../../scripts/lynx-sandbox.mjs';

const here = dirname(fileURLToPath(import.meta.url));

function pseudoRandom(length: number, seed: number): Uint8Array {
  const out = new Uint8Array(length);
  let x = seed >>> 0 || 1;
  for (let i = 0; i < length; i++) {
    x ^= x << 13;
    x ^= x >>> 17;
    x ^= x << 5;
    out[i] = x & 0xff;
  }
  return out;
}

describe('the client in a Lynx-shaped runtime', () => {
  it('runs codecs, hashing, transfers (JSON + MessagePack) and connection setup without the missing APIs', async () => {
    const code = await bundleForLynx(join(here, 'scenario.ts'));
    // Lynx has `fetch` in its core (Android/iOS 2.18+, desktop 3.7+), as a bundle-scope identifier.
    const result = (await runInLynxSandbox({ code, fetch })) as Record<string, any>;

    expect(result.globals).toEqual({
      BigInt: 'undefined',
      Intl: 'undefined',
      TextEncoder: 'undefined',
      URL: 'undefined',
      Blob: 'undefined',
      setTimeoutOnGlobal: 'undefined',
      setTimeoutInScope: 'function',
    });
    const text = 'سلام 😀 G9';
    expect(result.utf8).toEqual(Array.from(Buffer.from(text, 'utf8')));
    expect(result.utf8RoundTrip).toBe(true);
    expect(result.base64).toBe(Buffer.from(text, 'utf8').toString('base64'));
    expect(result.sha256).toBe(createHash('sha256').update(pseudoRandom(200 * 1024 + 3, 7)).digest('hex'));
    expect(result.transfers).toEqual({
      json: { upload: true, download: true },
      messagepack: { upload: true, download: true },
    });
    expect(result.clientPhase).toBe('disconnected');
    expect(result.blobDownload).toMatch(/downloadBytes/);
  });
});
