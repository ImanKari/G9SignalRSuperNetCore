import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import { describe, expect, it } from 'vitest';
import {
  G9DownloadErrorCodes,
  G9FileDownloader,
  G9TransferError,
  G9UploadStatus,
  getG9ErrorCode,
  type G9UploadRetryInfo,
} from '../src/index.js';
import { decodeChunk } from '../src/fileTransfer/downloader.js';
import { FakeHub, sameBytes } from './helpers/fakeHub.js';
import { DownloadServer, pseudoRandomBytes } from './helpers/transferServer.js';

const KiB = 1024;

async function bytesOf(blob: Blob): Promise<Uint8Array> {
  return new Uint8Array(await blob.arrayBuffer());
}

describe('G9FileDownloader', () => {
  it('assembles base64 chunks (JSON) into a Blob, verifies SHA-256 and reports progress', async () => {
    const content = pseudoRandomBytes(300 * KiB + 5, 21);
    const hub = new FakeHub();
    const server = new DownloadServer(hub, content);
    const progress: Array<[number, number]> = [];
    const blob = await new G9FileDownloader(hub.connection).download('u1/9f2c.bin', {
      onProgress: (received, total) => progress.push([received, total]),
      type: 'application/octet-stream',
    });

    expect(sameBytes(await bytesOf(blob), content)).toBe(true);
    expect(blob.type).toBe('application/octet-stream');
    expect(server.begins).toEqual([['u1/9f2c.bin', 0, 64 * KiB]]);
    expect(server.streamCalls).toEqual([['u1/9f2c.bin', 0, 64 * KiB]]);
    expect(progress.length).toBe(5);
    expect(progress.at(-1)).toEqual([content.length, content.length]);
  });

  it('assembles raw byte chunks (MessagePack) and honours the server chunk size', async () => {
    const content = pseudoRandomBytes(100 * KiB, 22);
    const hub = new FakeHub(new MessagePackHubProtocol());
    const server = new DownloadServer(hub, content, { chunkSize: 16 * KiB });
    const blob = await new G9FileDownloader(hub.connection).download('f.bin');
    expect(sameBytes(await bytesOf(blob), content)).toBe(true);
    expect(server.streamCalls[0]).toEqual(['f.bin', 0, 16 * KiB]);
  });

  it('flushes to Blob parts on the way and still returns every byte of a multi-MiB file', async () => {
    const content = pseudoRandomBytes(9 * 1024 * KiB + 3, 23);
    const hub = new FakeHub(new MessagePackHubProtocol());
    new DownloadServer(hub, content, { chunkSize: 256 * KiB });
    const blob = await new G9FileDownloader(hub.connection).download('big.bin');
    expect(blob.size).toBe(content.length);
    expect(sameBytes(await bytesOf(blob), content)).toBe(true);
  });

  it('resumes from the bytes already received after a stream failure', async () => {
    const content = pseudoRandomBytes(256 * KiB, 24);
    const hub = new FakeHub();
    const server = new DownloadServer(hub, content, { failAfterChunks: { 0: 2 } });
    const retries: G9UploadRetryInfo[] = [];
    const blob = await new G9FileDownloader(hub.connection, { onRetry: (r) => retries.push(r) }).download('f.bin');

    expect(sameBytes(await bytesOf(blob), content)).toBe(true);
    expect(retries).toHaveLength(1);
    expect(retries[0]).toMatchObject({ reason: 'attemptFailed', attempt: 0, bytesAlreadyOnServer: 128 * KiB, backoffMs: 200 });
    expect(server.begins.map((b) => b[1])).toEqual([0, 128 * KiB]);
    expect(server.streamCalls.map((s) => s[1])).toEqual([0, 128 * KiB]);
  });

  it('treats an early end of stream as interrupted and resumes', async () => {
    const content = pseudoRandomBytes(200 * KiB, 25);
    const hub = new FakeHub();
    new DownloadServer(hub, content, { endAfterChunks: { 0: 1 } });
    const retries: G9UploadRetryInfo[] = [];
    const blob = await new G9FileDownloader(hub.connection, { onRetry: (r) => retries.push(r) }).download('f.bin');
    expect(sameBytes(await bytesOf(blob), content)).toBe(true);
    expect(retries.map((r) => [r.reason, r.bytesAlreadyOnServer])).toEqual([['interrupted', 64 * KiB]]);
  });

  it('rejects with G9_DOWNLOAD_HASH_MISMATCH when the bytes do not match the announced SHA-256', async () => {
    const content = pseudoRandomBytes(10 * KiB, 26);
    const hub = new FakeHub();
    new DownloadServer(hub, content, { announcedSha256: 'aa'.repeat(32) });
    const error = await new G9FileDownloader(hub.connection).download('f.bin').catch((e: unknown) => e);
    expect(error).toBeInstanceOf(G9TransferError);
    expect(error).toMatchObject({ code: G9DownloadErrorCodes.DownloadHashMismatch, status: G9UploadStatus.Failed });
    expect(getG9ErrorCode(error)).toBe('G9_DOWNLOAD_HASH_MISMATCH');
  });

  it('skips verification when disabled', async () => {
    const content = pseudoRandomBytes(10 * KiB, 27);
    const hub = new FakeHub();
    new DownloadServer(hub, content, { announcedSha256: 'aa'.repeat(32) });
    const blob = await new G9FileDownloader(hub.connection, { verifySha256: false }).download('f.bin');
    expect(blob.size).toBe(content.length);
  });

  it('rejects with G9_DOWNLOAD_NOT_FOUND without streaming', async () => {
    const hub = new FakeHub();
    const server = new DownloadServer(hub, new Uint8Array(0), { notFound: true });
    await expect(new G9FileDownloader(hub.connection).download('missing.bin')).rejects.toMatchObject({
      code: 'G9_DOWNLOAD_NOT_FOUND',
    });
    expect(server.streamCalls).toHaveLength(0);
  });

  it('fails with G9_DOWNLOAD_FAILED once the retries are spent', async () => {
    const content = pseudoRandomBytes(200 * KiB, 28);
    const hub = new FakeHub();
    new DownloadServer(hub, content, { failAfterChunks: { 0: 1, 1: 1 } });
    await expect(new G9FileDownloader(hub.connection, { maxRetries: 1 }).download('f.bin')).rejects.toMatchObject({
      code: 'G9_DOWNLOAD_FAILED',
      status: G9UploadStatus.Failed,
      bytesWritten: 128 * KiB,
    });
  });

  it('aborts with an AbortError', async () => {
    const content = pseudoRandomBytes(2 * 1024 * KiB, 29);
    const hub = new FakeHub();
    new DownloadServer(hub, content);
    const controller = new AbortController();
    const promise = new G9FileDownloader(hub.connection).download('f.bin', {
      signal: controller.signal,
      onProgress: (received) => {
        if (received >= 128 * KiB) controller.abort();
      },
    });
    await expect(promise).rejects.toMatchObject({ name: 'AbortError' });
  });

  it('decodes every chunk shape a protocol can deliver', () => {
    expect(decodeChunk('AQID')).toEqual(new Uint8Array([1, 2, 3]));
    expect(decodeChunk(new Uint8Array([4, 5]))).toEqual(new Uint8Array([4, 5]));
    expect(decodeChunk(new Uint8Array([6, 7]).buffer)).toEqual(new Uint8Array([6, 7]));
    expect(decodeChunk([8, 9])).toEqual(new Uint8Array([8, 9]));
    expect(decodeChunk(null)).toEqual(new Uint8Array(0));
  });
});
