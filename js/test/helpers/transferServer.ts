/* eslint-disable @typescript-eslint/no-explicit-any */
import { Subject, type IStreamResult } from '@microsoft/signalr';
import { createHash } from 'node:crypto';
import { FakeHub, sleep } from './fakeHub.js';

export interface UploadServerOptions {
  /** Bytes of `source` the server already holds at the first BeginUpload (resume). */
  initialOffset?: number;
  alreadyCompleted?: boolean;
  /** Delay before each stream item is "serialized" (models a slow transport). */
  serializeDelayMs?: number;
  /** Per attempt index: fail the UploadChunks invocation after this many chunks. */
  failAfterChunks?: Record<number, number>;
  /** Per attempt index: answer Interrupted after this many chunks. */
  interruptAfterChunks?: Record<number, number>;
  /** Send an UploadProgress ack every N chunks. */
  ackEvery?: number;
  /** Send the status as a string enum name instead of a number. */
  statusAsString?: boolean;
  /** Use PascalCase property names (MessagePack server shape). */
  pascalCase?: boolean;
}

function shape(record: Record<string, unknown>, pascal: boolean | undefined): Record<string, unknown> {
  if (!pascal) return record;
  return Object.fromEntries(Object.entries(record).map(([k, v]) => [k.charAt(0).toUpperCase() + k.slice(1), v]));
}

/** Simulates the G9 upload hub methods (BeginUpload / UploadChunks / UploadProgress) on a FakeHub. */
export class UploadServer {
  stored = new Uint8Array(0);
  readonly wireItems: unknown[] = [];
  readonly begins: unknown[][] = [];
  readonly subjectErrors: string[] = [];
  maxPending = 0;
  attempts = 0;
  private pending = 0;

  constructor(
    readonly hub: FakeHub,
    readonly source: Uint8Array,
    readonly options: UploadServerOptions = {},
  ) {
    if (options.initialOffset) this.stored = source.slice(0, options.initialOffset);
    hub.methods.set('BeginUpload', (...args: unknown[]) => this.begin(args));
    hub.methods.set('UploadChunks', (uploadId: string, subject: IStreamResult<unknown>) => this.chunks(uploadId, subject));
  }

  private status(value: 0 | 1 | 2): number | string {
    if (!this.options.statusAsString) return value;
    return ['Completed', 'Interrupted', 'Failed'][value]!;
  }

  private append(bytes: Uint8Array): void {
    const next = new Uint8Array(this.stored.length + bytes.length);
    next.set(this.stored, 0);
    next.set(bytes, this.stored.length);
    this.stored = next;
  }

  private begin(args: unknown[]): Record<string, unknown> {
    this.begins.push(args);
    const [uploadId, , , chunkSize] = args as [string, string, number, number];
    return shape(
      {
        uploadId,
        bytesAlreadyReceived: this.stored.length,
        chunkSize,
        alreadyCompleted: this.options.alreadyCompleted ?? false,
        finalPath: this.options.alreadyCompleted ? '/srv/uploads/u1/done.bin' : null,
        storedFileName: this.options.alreadyCompleted ? 'u1/done.bin' : null,
      },
      this.options.pascalCase,
    );
  }

  private chunks(uploadId: string, subject: IStreamResult<unknown>): Promise<unknown> {
    const attempt = this.attempts++;
    const failAfter = this.options.failAfterChunks?.[attempt];
    const interruptAfter = this.options.interruptAfterChunks?.[attempt];
    return new Promise((resolve, reject) => {
      let queue: Promise<void> = Promise.resolve();
      let count = 0;
      let finished = false;
      subject.subscribe({
        next: (item) => {
          this.pending++;
          this.maxPending = Math.max(this.maxPending, this.pending);
          queue = queue.then(async () => {
            if (finished) return;
            if (this.options.serializeDelayMs) await sleep(this.options.serializeDelayMs);
            else await Promise.resolve();
            const wireItem = this.hub.roundTripStreamItem(item); // releases the uploader's slot
            this.pending--;
            this.wireItems.push(wireItem);
            const bytes = typeof wireItem === 'string' ? new Uint8Array(Buffer.from(wireItem, 'base64')) : (wireItem as Uint8Array);
            this.append(bytes);
            count++;
            if (this.options.ackEvery && count % this.options.ackEvery === 0) {
              this.hub.emit('UploadProgress', shape({ uploadId, bytesReceived: this.stored.length, totalBytes: 0 }, this.options.pascalCase));
            }
            if (failAfter === count) {
              finished = true;
              reject(new Error('Invocation canceled due to the underlying connection being closed.'));
            } else if (interruptAfter === count) {
              finished = true;
              resolve(shape({ status: this.status(1), bytesWritten: this.stored.length }, this.options.pascalCase));
            }
          });
        },
        complete: () => {
          queue = queue.then(() => {
            if (finished) return;
            finished = true;
            resolve(
              shape(
                {
                  status: this.status(0),
                  bytesWritten: this.stored.length,
                  sha256: createHash('sha256').update(this.stored).digest('hex'),
                  finalPath: '/srv/uploads/u1/9f2c.bin',
                  storedFileName: 'u1/9f2c.bin',
                  errorCode: null,
                  errorMessage: null,
                },
                this.options.pascalCase,
              ),
            );
          });
        },
        error: (error: any) => {
          queue = queue.then(() => {
            this.subjectErrors.push(String(error?.message ?? error));
            if (finished) return;
            finished = true;
            resolve(shape({ status: this.status(1), bytesWritten: this.stored.length }, this.options.pascalCase));
          });
        },
      });
    });
  }
}

export interface DownloadServerOptions {
  /** Per attempt index: error the stream after this many chunks. */
  failAfterChunks?: Record<number, number>;
  /** Per attempt index: complete the stream early after this many chunks. */
  endAfterChunks?: Record<number, number>;
  /** Announce this SHA-256 instead of the real one. */
  announcedSha256?: string;
  notFound?: boolean;
  chunkSize?: number;
}

/** Simulates BeginDownload / DownloadChunks on a FakeHub, sending items the way the protocol would deliver them. */
export class DownloadServer {
  readonly begins: unknown[][] = [];
  readonly streamCalls: unknown[][] = [];
  attempts = 0;

  constructor(
    readonly hub: FakeHub,
    readonly content: Uint8Array,
    readonly options: DownloadServerOptions = {},
  ) {
    hub.methods.set('BeginDownload', (fileName: string, resumeFrom: number, chunkSize: number) => {
      this.begins.push([fileName, resumeFrom, chunkSize]);
      return {
        notFound: options.notFound ?? false,
        totalBytes: content.length,
        sha256: options.announcedSha256 ?? createHash('sha256').update(content).digest('hex'),
        chunkSize: options.chunkSize ?? chunkSize,
        resumeFrom,
      };
    });
    hub.streams.set('DownloadChunks', (fileName: string, resumeFrom: number, chunkSize: number) => {
      this.streamCalls.push([fileName, resumeFrom, chunkSize]);
      const attempt = this.attempts++;
      const subject = new Subject<unknown>();
      const failAfter = options.failAfterChunks?.[attempt];
      const endAfter = options.endAfterChunks?.[attempt];
      void (async () => {
        await sleep(1);
        let count = 0;
        for (let offset = resumeFrom; offset < content.length; offset += chunkSize) {
          if (failAfter === count) {
            subject.error(new Error('Stream failed: connection lost.'));
            return;
          }
          if (endAfter === count) break;
          const chunk = content.slice(offset, Math.min(content.length, offset + chunkSize));
          // The .NET server writes byte[] as base64 under JSON (System.Text.Json) and as bin under MessagePack; the
          // client receives what the protocol round trip yields.
          const serverItem = this.hub.protocol.name === 'json' ? Buffer.from(chunk).toString('base64') : chunk;
          subject.next(this.hub.roundTripStreamItem(serverItem));
          count++;
          await Promise.resolve();
        }
        subject.complete();
      })();
      return subject;
    });
  }
}

export function pseudoRandomBytes(length: number, seed = 1): Uint8Array {
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

export function sha256HexNode(data: Uint8Array): string {
  return createHash('sha256').update(data).digest('hex');
}
