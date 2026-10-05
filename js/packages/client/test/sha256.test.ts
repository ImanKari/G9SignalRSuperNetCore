import { describe, expect, it } from 'vitest';
import { sha256Hex } from '../src/index.js';
import { Sha256, SHA256_SLICE_BYTES } from '../src/sha256.js';

const encoder = new TextEncoder();

async function webCryptoHex(data: Uint8Array): Promise<string> {
  const digest = await globalThis.crypto.subtle.digest('SHA-256', data as unknown as ArrayBuffer);
  return Buffer.from(digest).toString('hex');
}

function pseudoRandomBytes(length: number, seed = 1): Uint8Array {
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

describe('sha256Hex — known vectors (FIPS 180-4 / NIST)', () => {
  const vectors: Array<[string, string]> = [
    ['', 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'],
    ['abc', 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad'],
    [
      'abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq',
      '248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1',
    ],
    [
      'abcdefghbcdefghicdefghijdefghijkefghijklfghijklmghijklmnhijklmnoijklmnopjklmnopqklmnopqrlmnopqrsmnopqrstnopqrstu',
      'cf5b16a778af8380036ce59e7b0492370b249b11e8f07a51afac45037afee9d1',
    ],
    ['The quick brown fox jumps over the lazy dog', 'd7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592'],
  ];

  for (const [input, expected] of vectors) {
    it(`hashes ${JSON.stringify(input.length > 20 ? input.slice(0, 20) + '…' : input)}`, async () => {
      expect(await sha256Hex(encoder.encode(input))).toBe(expected);
      expect(await sha256Hex(new Blob([input]))).toBe(expected);
    });
  }

  it('hashes one million "a" (long NIST vector)', async () => {
    const data = new Uint8Array(1_000_000).fill(0x61);
    expect(await sha256Hex(data)).toBe('cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0');
  });
});

describe('sha256Hex — against WebCrypto', () => {
  const sizes = [0, 1, 55, 56, 63, 64, 65, 127, 128, 1000, 1024 * 1024 + 1];

  for (const size of sizes) {
    it(`matches WebCrypto for ${size} bytes (Uint8Array, ArrayBuffer and Blob)`, async () => {
      const data = pseudoRandomBytes(size, size + 7);
      const expected = await webCryptoHex(data);
      expect(await sha256Hex(data)).toBe(expected);
      expect(await sha256Hex(data.slice().buffer)).toBe(expected);
      expect(await sha256Hex(new Blob([data as BlobPart]))).toBe(expected);
    });
  }

  it('respects the byteOffset of a Uint8Array view', async () => {
    const backing = pseudoRandomBytes(200, 3);
    const view = backing.subarray(17, 150);
    expect(await sha256Hex(view)).toBe(await webCryptoHex(view.slice()));
  });

  it('gives the same digest whatever the update boundaries', async () => {
    const data = pseudoRandomBytes(10_000, 11);
    const expected = await webCryptoHex(data);
    for (const step of [1, 3, 63, 64, 65, 999]) {
      const hasher = new Sha256();
      for (let i = 0; i < data.length; i += step) hasher.update(data.subarray(i, i + step));
      expect(hasher.hexDigest()).toBe(expected);
    }
  });
});

describe('sha256Hex — slicing, progress and abort', () => {
  it('reads a Blob in 4 MiB slices and reports monotonic progress', async () => {
    const size = SHA256_SLICE_BYTES * 2 + 12345;
    const data = pseudoRandomBytes(size, 5);
    const progress: Array<[number, number]> = [];
    const hex = await sha256Hex(new Blob([data as BlobPart]), (done, total) => progress.push([done, total]));
    expect(hex).toBe(await webCryptoHex(data));
    expect(progress).toEqual([
      [SHA256_SLICE_BYTES, size],
      [SHA256_SLICE_BYTES * 2, size],
      [size, size],
    ]);
  });

  it('never reads more than one slice of a Blob at a time', async () => {
    const size = SHA256_SLICE_BYTES * 3;
    const blob = new Blob([pseudoRandomBytes(size, 9) as BlobPart]);
    const sliceSizes: number[] = [];
    const spy = new Proxy(blob, {
      get(target, property) {
        if (property === 'slice') {
          return (start: number, end: number) => {
            sliceSizes.push(end - start);
            return target.slice(start, end);
          };
        }
        const value = Reflect.get(target, property, target);
        return typeof value === 'function' ? value.bind(target) : value;
      },
    });
    await sha256Hex(spy);
    expect(sliceSizes).toEqual([SHA256_SLICE_BYTES, SHA256_SLICE_BYTES, SHA256_SLICE_BYTES]);
  });

  it('rejects with an AbortError when aborted', async () => {
    const controller = new AbortController();
    const data = new Blob([new Uint8Array(SHA256_SLICE_BYTES * 3)]);
    const promise = sha256Hex(data, (done) => {
      if (done >= SHA256_SLICE_BYTES) controller.abort();
    }, controller.signal);
    await expect(promise).rejects.toMatchObject({ name: 'AbortError' });
  });

  it('rejects at once for an already aborted signal', async () => {
    const controller = new AbortController();
    controller.abort();
    await expect(sha256Hex(new Uint8Array(10), undefined, controller.signal)).rejects.toMatchObject({ name: 'AbortError' });
  });
});
