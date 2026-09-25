import { bytesToBase64 } from '../internal/base64.js';

// SignalR JS has no backpressure on client-to-server streams: Subject.next() only chains the send of the item onto an
// internal promise queue, so a producer that just loops would queue the whole file in memory. The queue does
// serialize items one after the other, and only once every earlier item has been handed to the transport (and, with
// stateful reconnect, once the message buffer has room). The wrappers below report the moment their item is
// serialized, which gives the uploader an exact "sent" signal to bound the number of chunks in flight.
//
// - JSON: `byte[]` must travel as a base64 string (System.Text.Json reads byte[] from base64; JSON.stringify of a
//   Uint8Array would produce an object). The item is an object whose toJSON() returns the base64 text, so the wire
//   carries exactly that string, and the base64 is only built when the item is actually serialized.
// - MessagePack: raw bytes (bin). The item is a Uint8Array subclass sharing the chunk's memory; the msgpack encoder
//   reads its byteLength first, which is the signal.

/** A JSON-protocol stream item: serializes to the base64 string of `bytes`. */
export interface G9Base64Chunk {
  toJSON(): string;
}

/** Creates the stream item for one chunk; `onSerialized` is called once, when SignalR serializes it. */
export function createWireChunk(bytes: Uint8Array, binary: boolean, onSerialized: () => void): unknown {
  let fired = false;
  const fire = (): void => {
    if (fired) return;
    fired = true;
    try {
      onSerialized();
    } catch {
      // Never break the serializer.
    }
  };

  if (!binary) {
    const chunk: G9Base64Chunk = {
      toJSON() {
        fire();
        return bytesToBase64(bytes);
      },
    };
    return chunk;
  }

  const tracked = new G9TrackedChunk(bytes.buffer as ArrayBuffer, bytes.byteOffset, bytes.length);
  trackers.set(tracked, fire);
  return tracked;
}

const trackers = new WeakMap<Uint8Array, () => void>();

/** Uint8Array that reports the first read of its size/backing store (what a binary encoder does first). */
export class G9TrackedChunk extends Uint8Array {}

const typedArrayPrototype = Object.getPrototypeOf(Uint8Array.prototype) as object;
for (const property of ['byteLength', 'length', 'buffer', 'byteOffset'] as const) {
  const descriptor = Object.getOwnPropertyDescriptor(typedArrayPrototype, property);
  const getter = descriptor?.get;
  if (!getter) continue;
  Object.defineProperty(G9TrackedChunk.prototype, property, {
    configurable: true,
    enumerable: false,
    get(this: Uint8Array) {
      trackers.get(this)?.();
      return getter.call(this);
    },
  });
}
