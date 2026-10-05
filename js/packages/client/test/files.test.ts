// Byte sources (what Lynx uploads from), in-memory byte downloads, and the .NET-twin file transfers over a G9FileSystem.
import { createHash } from 'node:crypto';
import { mkdtemp, readdir, readFile, rm, stat, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import {
  bytesSource,
  G9DownloadErrorCodes,
  G9FileDownloader,
  G9FileUploader,
  G9UploadStatus,
  sha256Hex,
  type G9ByteSource,
  type G9FileSystem,
} from '../src/index.js';
import { nodeFileSource, nodeFileSystem } from '../src/node/index.js';
import { FakeHub, sameBytes } from './helpers/fakeHub.js';
import { DownloadServer, pseudoRandomBytes, sha256HexNode, UploadServer } from './helpers/transferServer.js';

const KiB = 1024;
let dir = '';

beforeEach(async () => {
  dir = await mkdtemp(join(tmpdir(), 'g9-files-'));
});
afterEach(async () => {
  await rm(dir, { recursive: true, force: true });
});

describe('byte sources', () => {
  it('hash a source in slices exactly like Node', async () => {
    const content = pseudoRandomBytes(9 * 1024 * KiB + 11, 31);
    const reads: Array<[number, number]> = [];
    const source: G9ByteSource = {
      size: content.length,
      read: (offset, length) => {
        reads.push([offset, length]);
        return Promise.resolve(content.slice(offset, offset + length).buffer);
      },
    };
    expect(await sha256Hex(source)).toBe(sha256HexNode(content));
    expect(reads.map((r) => r[1])).toEqual([4 * 1024 * KiB, 4 * 1024 * KiB, 1024 * KiB + 11]);
  });

  it('fail a hash when the source ends early instead of looping', async () => {
    const source: G9ByteSource = { size: 10, read: () => Promise.resolve(new Uint8Array(0)) };
    await expect(sha256Hex(source)).rejects.toThrow(/ended after 0 of 10/);
  });

  it('upload from memory with the name-based upload id', async () => {
    const content = pseudoRandomBytes(150 * KiB, 32);
    const hub = new FakeHub();
    const server = new UploadServer(hub, content);
    const result = await new G9FileUploader(hub.connection).upload(
      bytesSource(content, { name: 'report.pdf', lastModified: 1_700_000_000_000 }),
    );
    expect(result.status).toBe(G9UploadStatus.Completed);
    expect(sameBytes(server.stored, content)).toBe(true);
    const begin = server.begins[0]!;
    expect(begin[0]).toBe(G9FileUploader.computeUploadId('report.pdf', content.length, 1_700_000_000_000));
    expect(begin[1]).toBe('report.pdf');
  });
});

describe('files on disk (the .NET-twin path)', () => {
  it('upload a file by path with the .NET upload id: sha256(lower(fullPath)|length|lastWriteTicks)', async () => {
    const path = join(dir, 'Photo.JPG');
    const content = pseudoRandomBytes(70 * KiB, 33);
    await writeFile(path, content);
    const hub = new FakeHub();
    const server = new UploadServer(hub, content);

    const result = await new G9FileUploader(hub.connection).uploadFile(path, { fileSystem: nodeFileSystem });

    expect(result.status).toBe(G9UploadStatus.Completed);
    expect(sameBytes(server.stored, content)).toBe(true);
    const info = await stat(path, { bigint: true });
    const ticks = 621355968000000000n + info.mtimeNs / 100n;
    const key = `${resolve(path).toLowerCase()}|${content.length}|${ticks}`;
    expect(server.begins[0]![0]).toBe(createHash('sha256').update(key, 'utf8').digest('hex'));
    expect(server.begins[0]![1]).toBe('Photo.JPG');
    expect((await nodeFileSource(path)).uploadKey).toBe(key);
  });

  it('refuse a missing file like .NET (FileNotFound)', async () => {
    const hub = new FakeHub();
    await expect(
      new G9FileUploader(hub.connection).uploadFile(join(dir, 'missing.bin'), { fileSystem: nodeFileSystem }),
    ).rejects.toThrow(/not found/);
  });

  it('download bytes in memory on any runtime', async () => {
    const content = pseudoRandomBytes(200 * KiB + 3, 34);
    const hub = new FakeHub();
    new DownloadServer(hub, content);
    const bytes = await new G9FileDownloader(hub.connection).downloadBytes('f.bin');
    expect(sameBytes(bytes, content)).toBe(true);
  });

  it('download to a file through .partial, verify and rename', async () => {
    const content = pseudoRandomBytes(3 * 1024 * KiB + 17, 35);
    const hub = new FakeHub();
    new DownloadServer(hub, content, { chunkSize: 128 * KiB });
    const target = join(dir, 'sub', 'out.bin');
    const progress: number[] = [];

    const result = await new G9FileDownloader(hub.connection).downloadToFile('u/out.bin', target, {
      fileSystem: nodeFileSystem,
      onProgress: (p) => progress.push(p.bytesReceived),
    });

    expect(result).toMatchObject({ status: G9UploadStatus.Completed, bytesWritten: content.length, localPath: target });
    expect(result.sha256).toBe(sha256HexNode(content));
    expect(sameBytes(new Uint8Array(await readFile(target)), content)).toBe(true);
    expect(await readdir(join(dir, 'sub'))).toEqual(['out.bin']);
    expect(progress.at(-1)).toBe(content.length);
  });

  it('resume from the .partial a previous call left behind (across calls, like .NET)', async () => {
    const content = pseudoRandomBytes(600 * KiB, 36);
    const target = join(dir, 'resume.bin');
    await writeFile(target + '.partial', content.subarray(0, 200 * KiB));
    const hub = new FakeHub();
    const server = new DownloadServer(hub, content);

    const result = await new G9FileDownloader(hub.connection).downloadToFile('r.bin', target, { fileSystem: nodeFileSystem });

    expect(result.status).toBe(G9UploadStatus.Completed);
    expect(server.begins[0]![1]).toBe(200 * KiB);
    expect(server.streamCalls[0]![1]).toBe(200 * KiB);
    expect(sameBytes(new Uint8Array(await readFile(target)), content)).toBe(true);
  });

  it('resume within a call after a broken stream, keeping what arrived', async () => {
    const content = pseudoRandomBytes(512 * KiB, 37);
    const hub = new FakeHub();
    const server = new DownloadServer(hub, content, { failAfterChunks: { 0: 3 } });
    const target = join(dir, 'broken.bin');
    const result = await new G9FileDownloader(hub.connection).downloadToFile('b.bin', target, { fileSystem: nodeFileSystem });
    expect(result.status).toBe(G9UploadStatus.Completed);
    expect(server.streamCalls.map((c) => c[1])).toEqual([0, 192 * KiB]);
    expect(sameBytes(new Uint8Array(await readFile(target)), content)).toBe(true);
  });

  it('short-circuit when the target already holds the server file', async () => {
    const content = pseudoRandomBytes(100 * KiB, 38);
    const target = join(dir, 'same.bin');
    await writeFile(target, content);
    const hub = new FakeHub();
    const server = new DownloadServer(hub, content);
    const result = await new G9FileDownloader(hub.connection).downloadToFile('s.bin', target, { fileSystem: nodeFileSystem });
    expect(result).toMatchObject({ status: G9UploadStatus.Completed, localPath: target });
    expect(server.streamCalls).toHaveLength(0);
  });

  it('never clobber an unrelated target: the new file gets a UTC stamp', async () => {
    const content = pseudoRandomBytes(10 * KiB, 39);
    const target = join(dir, 'doc.txt');
    await writeFile(target, 'unrelated');
    const hub = new FakeHub();
    new DownloadServer(hub, content);
    const result = await new G9FileDownloader(hub.connection).downloadToFile('d.txt', target, { fileSystem: nodeFileSystem });
    expect(result.status).toBe(G9UploadStatus.Completed);
    expect(result.localPath).toMatch(/[\\/]doc\.\d{17}\.txt$/);
    expect(await readFile(target, 'utf8')).toBe('unrelated');
    expect(sameBytes(new Uint8Array(await readFile(result.localPath!)), content)).toBe(true);
  });

  it('report a hash mismatch as a Failed result and drop the partial', async () => {
    const content = pseudoRandomBytes(10 * KiB, 40);
    const hub = new FakeHub();
    new DownloadServer(hub, content, { announcedSha256: '00'.repeat(32) });
    const target = join(dir, 'bad.bin');
    const result = await new G9FileDownloader(hub.connection).downloadToFile('x', target, { fileSystem: nodeFileSystem });
    expect(result).toMatchObject({ status: G9UploadStatus.Failed, errorCode: G9DownloadErrorCodes.DownloadHashMismatch });
    expect(await readdir(dir)).toEqual([]);
  });

  it('report a missing server file as a Failed result', async () => {
    const hub = new FakeHub();
    new DownloadServer(hub, new Uint8Array(1), { notFound: true });
    const result = await new G9FileDownloader(hub.connection).downloadToFile('nope', join(dir, 'n.bin'), {
      fileSystem: nodeFileSystem,
    });
    expect(result).toMatchObject({ status: G9UploadStatus.Failed, errorCode: G9DownloadErrorCodes.DownloadNotFound });
  });

  it('end a cancelled transfer as Interrupted and keep the partial for the next call', async () => {
    const content = pseudoRandomBytes(2 * 1024 * KiB, 41);
    const hub = new FakeHub();
    new DownloadServer(hub, content, { chunkSize: 64 * KiB });
    const target = join(dir, 'cancel.bin');
    const controller = new AbortController();
    const writes: number[] = [];
    const tracking: G9FileSystem = {
      ...nodeFileSystem,
      write: async (path, offset, bytes, truncate) => {
        writes.push(offset);
        await nodeFileSystem.write(path, offset, bytes, truncate);
      },
    };
    const downloader = new G9FileDownloader(hub.connection);
    const result = await downloader.downloadToFile('c.bin', target, {
      fileSystem: tracking,
      signal: controller.signal,
      onProgress: (p) => {
        if (p.bytesReceived >= 1024 * KiB) controller.abort();
      },
    });
    expect(result.status).toBe(G9UploadStatus.Interrupted);
    const kept = (await stat(target + '.partial')).size;
    expect(kept).toBe(result.bytesWritten);
    expect(kept).toBeGreaterThanOrEqual(1024 * KiB);
    expect(sameBytes(new Uint8Array(await readFile(target + '.partial')), content.subarray(0, kept))).toBe(true);

    const second = await new G9FileDownloader(hub.connection).downloadToFile('c.bin', target, { fileSystem: nodeFileSystem });
    expect(second.status).toBe(G9UploadStatus.Completed);
    expect(sameBytes(new Uint8Array(await readFile(target)), content)).toBe(true);
  });

  it('drop a stale tail past the resume point on the first write of an attempt', async () => {
    const content = pseudoRandomBytes(300 * KiB, 42);
    const target = join(dir, 'tail.bin');
    const hub = new FakeHub();
    new DownloadServer(hub, content);
    // A previous crash left more bytes than it reported? The partial is always the resume point, so write past it.
    await writeFile(target + '.partial', content.subarray(0, 100 * KiB));
    const result = await new G9FileDownloader(hub.connection).downloadToFile('t.bin', target, { fileSystem: nodeFileSystem });
    expect(result.status).toBe(G9UploadStatus.Completed);
    expect((await stat(target)).size).toBe(content.length);
  });
});
