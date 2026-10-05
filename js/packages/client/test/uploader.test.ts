import { HubConnectionState, JsonHubProtocol, MessageType } from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import { describe, expect, it } from 'vitest';
import {
  G9Client,
  G9FileUploader,
  G9UploadStatus,
  sha256Hex,
  type G9UploadProgress,
  type G9UploadRetryInfo,
} from '../src/index.js';
import { createWireChunk } from '../src/fileTransfer/wireChunk.js';
import { FakeHub, sameBytes, sleep } from './helpers/fakeHub.js';
import { pseudoRandomBytes, sha256HexNode, UploadServer } from './helpers/transferServer.js';

const KiB = 1024;

function fileOf(data: Uint8Array, name = 'photo.jpg', lastModified = 1_700_000_000_000): File {
  return new File([data as BlobPart], name, { lastModified });
}

describe('wire chunks', () => {
  it('serialize to a base64 string under the real JSON protocol, reporting serialization once', () => {
    const bytes = pseudoRandomBytes(1000, 2);
    let serialized = 0;
    const item = createWireChunk(bytes, false, () => serialized++);
    expect(serialized).toBe(0);
    const text = new JsonHubProtocol().writeMessage({ type: MessageType.StreamItem, invocationId: '7', item }) as string;
    expect(serialized).toBe(1);
    const parsed = JSON.parse(text.slice(0, -1)) as { item: unknown };
    expect(typeof parsed.item).toBe('string');
    expect(new Uint8Array(Buffer.from(parsed.item as string, 'base64'))).toEqual(bytes);
  });

  it('serialize to raw bin under the real MessagePack protocol, reporting serialization once', () => {
    const backing = pseudoRandomBytes(5000, 3);
    const bytes = backing.subarray(100, 4100);
    let serialized = 0;
    const item = createWireChunk(bytes, true, () => serialized++);
    expect(serialized).toBe(0);
    const protocol = new MessagePackHubProtocol();
    const wire = protocol.writeMessage({ type: MessageType.StreamItem, invocationId: '7', item });
    expect(serialized).toBe(1);
    const [message] = protocol.parseMessages(wire as ArrayBuffer, { log: () => undefined });
    const received = (message as { item: unknown }).item;
    expect(received).toBeInstanceOf(Uint8Array);
    expect(new Uint8Array(received as Uint8Array)).toEqual(new Uint8Array(bytes));
  });
});

describe('G9FileUploader', () => {
  it('derives the upload id from name|size|lastModified like .NET derives it from path|length|ticks', async () => {
    const data = pseudoRandomBytes(1000);
    const hub = new FakeHub();
    const server = new UploadServer(hub, data);
    await new G9FileUploader(hub.connection).upload(fileOf(data, 'a.bin', 123), { serverAck: false });
    const expected = await sha256Hex(new TextEncoder().encode('a.bin|1000|123'));
    expect(server.begins[0]![0]).toBe(expected);
    expect(G9FileUploader.computeUploadId('a.bin', 1000, 123)).toBe(expected);
  });

  it('calls BeginUpload with (uploadId, fileName, totalBytes, chunkSize, sha256) and streams base64 chunks under JSON', async () => {
    const data = pseudoRandomBytes(200 * KiB + 17, 4);
    const hub = new FakeHub();
    const server = new UploadServer(hub, data);
    const result = await new G9FileUploader(hub.connection).upload(fileOf(data));

    const [uploadId, fileName, totalBytes, chunkSize, sha] = server.begins[0]!;
    expect(typeof uploadId).toBe('string');
    expect(fileName).toBe('photo.jpg');
    expect(totalBytes).toBe(data.length);
    expect(chunkSize).toBe(64 * KiB);
    expect(sha).toBe(sha256HexNode(data));

    expect(server.wireItems.length).toBe(4);
    for (const item of server.wireItems) {
      expect(typeof item).toBe('string');
      expect(Buffer.from(item as string, 'base64').length).toBeLessThanOrEqual(64 * KiB);
    }
    expect(sameBytes(server.stored, data)).toBe(true);
    expect(result).toMatchObject({
      status: G9UploadStatus.Completed,
      bytesWritten: data.length,
      sha256: sha256HexNode(data),
      storedFileName: 'u1/9f2c.bin',
      finalPath: '/srv/uploads/u1/9f2c.bin',
    });
  });

  it('sends raw bytes under MessagePack and reads PascalCase DTOs', async () => {
    const data = pseudoRandomBytes(150 * KiB, 5);
    const hub = new FakeHub(new MessagePackHubProtocol());
    const server = new UploadServer(hub, data, { pascalCase: true, ackEvery: 1 });
    const acks: number[] = [];
    const result = await new G9FileUploader(hub.connection).upload(fileOf(data), {
      onProgress: (p) => {
        if (p.phase === 'uploading') acks.push(p.bytesAcknowledged);
      },
    });
    expect(server.wireItems.length).toBe(3);
    for (const item of server.wireItems) expect(item).toBeInstanceOf(Uint8Array);
    expect(sameBytes(server.stored, data)).toBe(true);
    expect(result.status).toBe(G9UploadStatus.Completed);
    expect(result.storedFileName).toBe('u1/9f2c.bin');
    expect(Math.max(...acks)).toBe(data.length);
  });

  it('honours the resume offset returned by BeginUpload', async () => {
    const data = pseudoRandomBytes(300 * KiB, 6);
    const offset = 100_000;
    const hub = new FakeHub();
    const server = new UploadServer(hub, data, { initialOffset: offset });
    const progress: G9UploadProgress[] = [];
    const result = await new G9FileUploader(hub.connection).upload(fileOf(data), { onProgress: (p) => progress.push(p) });

    const firstChunk = new Uint8Array(Buffer.from(server.wireItems[0] as string, 'base64'));
    expect(sameBytes(firstChunk, data.slice(offset, offset + 64 * KiB))).toBe(true);
    expect(sameBytes(server.stored, data)).toBe(true);
    expect(result.status).toBe(G9UploadStatus.Completed);

    const uploading = progress.filter((p) => p.phase === 'uploading');
    expect(uploading[0]!.bytesSent).toBe(offset);
    expect(uploading.at(-1)!.bytesSent).toBe(data.length);
    expect(progress.some((p) => p.phase === 'hashing' && p.bytesSent === data.length)).toBe(true);
  });

  it('uses the chunk size the server imposes', async () => {
    const data = pseudoRandomBytes(100 * KiB, 7);
    const hub = new FakeHub();
    const server = new UploadServer(hub, data);
    hub.methods.set('BeginUpload', (uploadId: string) => ({ uploadId, bytesAlreadyReceived: 0, chunkSize: 10 * KiB }));
    await new G9FileUploader(hub.connection).upload(fileOf(data), { serverAck: false });
    expect(server.wireItems.length).toBe(10);
  });

  it('returns Completed without streaming when the server already has the file', async () => {
    const data = pseudoRandomBytes(1000, 8);
    const hub = new FakeHub();
    const server = new UploadServer(hub, data, { alreadyCompleted: true });
    const result = await new G9FileUploader(hub.connection).upload(fileOf(data));
    expect(result).toEqual({
      status: G9UploadStatus.Completed,
      bytesWritten: 1000,
      sha256: sha256HexNode(data),
      finalPath: '/srv/uploads/u1/done.bin',
      storedFileName: 'u1/done.bin',
      errorCode: null,
      errorMessage: null,
    });
    expect(hub.callsOf('UploadChunks')).toHaveLength(0);
    expect(server.attempts).toBe(0);
  });

  it('retries a failed attempt with the .NET backoff and resumes from the server offset', async () => {
    const data = pseudoRandomBytes(256 * KiB, 9);
    const hub = new FakeHub();
    const server = new UploadServer(hub, data, { failAfterChunks: { 0: 2 } });
    const retries: G9UploadRetryInfo[] = [];
    const result = await new G9FileUploader(hub.connection, { onRetry: (r) => retries.push(r) }).upload(fileOf(data));

    expect(retries).toHaveLength(1);
    expect(retries[0]).toMatchObject({ reason: 'attemptFailed', attempt: 0, maxRetries: 5, bytesAlreadyOnServer: 0, backoffMs: 200 });
    expect(retries[0]!.error).toBeInstanceOf(Error);
    expect(server.begins).toHaveLength(2);
    expect(sameBytes(server.stored, data)).toBe(true);
    expect(result.status).toBe(G9UploadStatus.Completed);
  });

  it('retries an Interrupted result (string status accepted) and then succeeds', async () => {
    const data = pseudoRandomBytes(200 * KiB, 10);
    const hub = new FakeHub();
    const server = new UploadServer(hub, data, { interruptAfterChunks: { 0: 1, 1: 1 }, statusAsString: true });
    const retries: G9UploadRetryInfo[] = [];
    const result = await new G9FileUploader(hub.connection, { onRetry: (r) => retries.push(r) }).upload(fileOf(data));

    expect(retries.map((r) => [r.reason, r.attempt, r.backoffMs, r.bytesAlreadyOnServer])).toEqual([
      ['interrupted', 0, 200, 64 * KiB],
      ['interrupted', 1, 400, 128 * KiB],
    ]);
    expect(sameBytes(server.stored, data)).toBe(true);
    expect(result.status).toBe(G9UploadStatus.Completed);
  });

  it('gives up after maxRetries and rejects with the last error', async () => {
    const data = pseudoRandomBytes(200 * KiB, 11);
    const hub = new FakeHub();
    new UploadServer(hub, data, { failAfterChunks: { 0: 1, 1: 1 } });
    const retries: G9UploadRetryInfo[] = [];
    await expect(
      new G9FileUploader(hub.connection, { maxRetries: 1, onRetry: (r) => retries.push(r) }).upload(fileOf(data)),
    ).rejects.toThrow(/underlying connection being closed/);
    expect(retries).toHaveLength(1);
  });

  it('keeps at most maxChunksInFlight chunks waiting in the SignalR send queue', async () => {
    const data = pseudoRandomBytes(2 * 1024 * KiB, 12); // 32 chunks
    const hub = new FakeHub();
    const server = new UploadServer(hub, data, { serializeDelayMs: 2 });
    await new G9FileUploader(hub.connection).upload(fileOf(data), { serverAck: false });
    expect(sameBytes(server.stored, data)).toBe(true);
    expect(server.maxPending).toBeLessThanOrEqual(8);
    expect(server.maxPending).toBeGreaterThanOrEqual(2);

    const hub2 = new FakeHub();
    const server2 = new UploadServer(hub2, data, { serializeDelayMs: 1 });
    await new G9FileUploader(hub2.connection, { maxChunksInFlight: 2 }).upload(fileOf(data), { serverAck: false });
    expect(server2.maxPending).toBeLessThanOrEqual(2);
    expect(sameBytes(server2.stored, data)).toBe(true);
  });

  it('aborts: rejects with an AbortError and fails the stream so the server keeps the partial', async () => {
    const data = pseudoRandomBytes(1024 * KiB, 13);
    const hub = new FakeHub();
    const server = new UploadServer(hub, data, { serializeDelayMs: 3 });
    const controller = new AbortController();
    const promise = new G9FileUploader(hub.connection).upload(fileOf(data), {
      signal: controller.signal,
      onProgress: (p) => {
        if (p.phase === 'uploading' && p.bytesSent >= 3 * 64 * KiB) controller.abort();
      },
    });
    await expect(promise).rejects.toMatchObject({ name: 'AbortError' });
    await sleep(80);
    expect(server.subjectErrors).toEqual(['The upload was cancelled by the client.']);
    expect(server.stored.length).toBeLessThan(data.length);
    expect(hub.handlerCount('UploadProgress')).toBe(0);
  });

  it('aborts during hashing without calling the server', async () => {
    const data = pseudoRandomBytes(9 * 1024 * KiB, 14);
    const hub = new FakeHub();
    new UploadServer(hub, data);
    const controller = new AbortController();
    const promise = new G9FileUploader(hub.connection).upload(fileOf(data), {
      signal: controller.signal,
      onProgress: (p) => {
        if (p.phase === 'hashing') controller.abort();
      },
    });
    await expect(promise).rejects.toMatchObject({ name: 'AbortError' });
    expect(hub.calls).toHaveLength(0);
  });

  it('removes its UploadProgress handler afterwards and skips hashing when sha256 is given', async () => {
    const data = pseudoRandomBytes(1000, 15);
    const hub = new FakeHub();
    const server = new UploadServer(hub, data);
    const phases = new Set<string>();
    await new G9FileUploader(hub.connection).upload(fileOf(data), { sha256: 'ABC123', onProgress: (p) => phases.add(p.phase) });
    expect(server.begins[0]![4]).toBe('abc123');
    expect(phases.has('hashing')).toBe(false);
    expect(hub.handlerCount('UploadProgress')).toBe(0);
  });

  it('refuses a zero-byte file like .NET', async () => {
    const hub = new FakeHub();
    await expect(new G9FileUploader(hub.connection).upload(new Blob([]))).rejects.toThrow(/zero-byte/);
  });

  it('accepts a G9Client as target and waits for a reconnect before BeginUpload', async () => {
    const data = pseudoRandomBytes(1000, 16);
    const hub = new FakeHub();
    const server = new UploadServer(hub, data);
    const client = new G9Client({ url: 'http://test/hub', connectionFactory: () => hub.connection });
    hub.state = HubConnectionState.Reconnecting;
    const promise = new G9FileUploader(client).upload(fileOf(data), { sha256: sha256HexNode(data) });
    await sleep(30);
    expect(server.begins).toHaveLength(0);
    hub.simulateReconnected('conn-9');
    const result = await promise;
    expect(result.status).toBe(G9UploadStatus.Completed);
    expect(sameBytes(server.stored, data)).toBe(true);
  });
});
