// Runs INSIDE the Lynx-shaped sandbox (scripts/lynx-sandbox.mjs): no BigInt, Intl, TextEncoder, URL, Blob, btoa,
// WebSocket or performance; timers exist only as bundle-scope identifiers. Everything here is bundled with the library
// sources, so a dependency on a missing API fails this run.
import { HubConnectionState, JsonHubProtocol, Subject, type IHubProtocol, type IStreamResult } from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import {
  base64ToBytes,
  bytesSource,
  bytesToBase64,
  G9Client,
  G9FileDownloader,
  G9FileUploader,
  G9UploadStatus,
  sha256Hex,
  utf8Decode,
  utf8Encode,
} from '../../src/index.js';
import { Sha256 } from '../../src/sha256.js';

/** A minimal HubConnection double with scripted upload/download hub methods (no Node APIs). */
class SandboxHub {
  state = HubConnectionState.Connected;
  connectionId = 'sandbox';
  stored = new Uint8Array(0);
  private readonly handlers = new Map<string, Array<(...args: unknown[]) => void>>();

  constructor(
    readonly protocol: IHubProtocol,
    private readonly content: Uint8Array,
  ) {}

  on(method: string, handler: (...args: unknown[]) => void): void {
    const list = this.handlers.get(method) ?? [];
    list.push(handler);
    this.handlers.set(method, list);
  }

  off(method: string, handler?: (...args: unknown[]) => void): void {
    const list = this.handlers.get(method) ?? [];
    this.handlers.set(
      method,
      handler ? list.filter((h) => h !== handler) : [],
    );
  }

  onclose(): void {}
  onreconnecting(): void {}
  onreconnected(): void {}

  private roundTrip(item: unknown): unknown {
    // Through the real protocol, as a server would send it.
    const wire = this.protocol.writeMessage({ type: 2, invocationId: '1', item } as never);
    const [message] = this.protocol.parseMessages(wire as never, { log: () => undefined });
    return (message as { item: unknown }).item;
  }

  async invoke(method: string, ...args: unknown[]): Promise<unknown> {
    if (method === 'BeginUpload') {
      return { uploadId: args[0], bytesAlreadyReceived: this.stored.length, chunkSize: args[3], alreadyCompleted: false };
    }
    if (method === 'UploadChunks') {
      const subject = args[1] as Subject<unknown>;
      const binary = this.protocol.name === 'messagepack';
      await new Promise<void>((resolve, reject) => {
        subject.subscribe({
          next: (item) => {
            const raw = this.roundTrip(item);
            const bytes = binary ? new Uint8Array(raw as ArrayBufferLike) : base64ToBytes(raw as string);
            const next = new Uint8Array(this.stored.length + bytes.length);
            next.set(this.stored, 0);
            next.set(bytes, this.stored.length);
            this.stored = next;
          },
          error: reject,
          complete: () => resolve(),
        });
      });
      const hash = new Sha256().update(this.stored).hexDigest();
      return { status: 0, bytesWritten: this.stored.length, sha256: hash, storedFileName: 'stored.bin' };
    }
    if (method === 'BeginDownload') {
      return {
        notFound: false,
        totalBytes: this.content.length,
        sha256: new Sha256().update(this.content).hexDigest(),
        chunkSize: args[2],
        resumeFrom: args[1],
      };
    }
    throw new Error(`Unexpected invoke ${method}`);
  }

  stream<T>(method: string, ...args: unknown[]): IStreamResult<T> {
    if (method !== 'DownloadChunks') throw new Error(`Unexpected stream ${method}`);
    const subject = new Subject<unknown>();
    const resumeFrom = args[1] as number;
    const chunkSize = args[2] as number;
    setTimeout(() => {
      for (let offset = resumeFrom; offset < this.content.length; offset += chunkSize) {
        const chunk = this.content.slice(offset, Math.min(this.content.length, offset + chunkSize));
        subject.next(this.roundTrip(this.protocol.name === 'json' ? bytesToBase64(chunk) : chunk));
      }
      subject.complete();
    }, 0);
    return subject as unknown as IStreamResult<T>;
  }
}

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

function equal(a: Uint8Array, b: Uint8Array): boolean {
  if (a.length !== b.length) return false;
  for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return false;
  return true;
}

export default async function run(): Promise<Record<string, unknown>> {
  const g = globalThis as Record<string, unknown>;
  const out: Record<string, unknown> = {
    globals: {
      BigInt: typeof g.BigInt,
      Intl: typeof g.Intl,
      TextEncoder: typeof g.TextEncoder,
      URL: typeof g.URL,
      Blob: typeof g.Blob,
      setTimeoutOnGlobal: typeof g.setTimeout,
      setTimeoutInScope: typeof setTimeout,
    },
  };

  const text = 'سلام 😀 G9';
  out.utf8 = Array.from(utf8Encode(text));
  out.utf8RoundTrip = utf8Decode(utf8Encode(text)) === text;
  out.base64 = bytesToBase64(utf8Encode(text));

  const content = pseudoRandom(200 * 1024 + 3, 7);
  out.sha256 = await sha256Hex(bytesSource(content));

  const transfers: Record<string, unknown> = {};
  for (const protocol of [new JsonHubProtocol(), new MessagePackHubProtocol()]) {
    const hub = new SandboxHub(protocol, content);
    const result = await new G9FileUploader(hub as never).upload(bytesSource(content, { name: 'a.bin' }));
    const downloaded = await new G9FileDownloader(hub as never).downloadBytes('a.bin');
    transfers[protocol.name] = {
      upload: result.status === G9UploadStatus.Completed && equal(hub.stored, content),
      download: equal(downloaded, content),
    };
  }
  out.transfers = transfers;

  class NativeSocket {}
  const client = new G9Client({ url: 'https://host/hub', transport: 'websockets', webSocket: NativeSocket, skipNegotiation: true });
  out.clientPhase = client.state.phase;

  try {
    await new G9FileDownloader(new SandboxHub(new JsonHubProtocol(), content) as never).download('x');
    out.blobDownload = 'no error';
  } catch (error) {
    out.blobDownload = (error as Error).message;
  }
  return out;
}
