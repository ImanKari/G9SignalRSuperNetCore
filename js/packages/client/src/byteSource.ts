/**
 * Bytes the uploader and the hasher can read in slices, whatever holds them: a browser `Blob`/`File`, memory, or a file
 * a platform layer reads (a Lynx native module, Node's `fs`). Lynx has no `Blob`, so this is what a Lynx app passes.
 */
export interface G9ByteSource {
  /** Total size in bytes. */
  readonly size: number;
  /** File name declared to the server (uploads). */
  readonly name?: string;
  /** Last modification time (epoch ms), part of the default upload id. */
  readonly lastModified?: number;
  /**
   * Stable identity key that replaces `name|size|lastModified` as the default upload id input (a file system sets it to
   * the .NET key `lower(fullPath)|length|lastWriteTicks`, so the same file gets the same id as the .NET uploader).
   */
  readonly uploadKey?: string;
  /** Reads `length` bytes from `offset` (fewer only at the end of the data). */
  read(offset: number, length: number): Promise<Uint8Array | ArrayBuffer>;
}

interface BlobLike {
  readonly size: number;
  readonly name?: unknown;
  readonly lastModified?: unknown;
  slice(start?: number, end?: number): { arrayBuffer(): Promise<ArrayBuffer> };
}

/** True for a `Blob`/`File` (duck-typed: Lynx and some WebViews have no `Blob` global to test against). */
export function isBlobLike(value: unknown): value is BlobLike {
  return (
    typeof value === 'object' &&
    value !== null &&
    typeof (value as BlobLike).size === 'number' &&
    typeof (value as BlobLike).slice === 'function' &&
    typeof (value as { read?: unknown }).read !== 'function'
  );
}

/** True for a {@link G9ByteSource}. */
export function isByteSource(value: unknown): value is G9ByteSource {
  return (
    typeof value === 'object' &&
    value !== null &&
    typeof (value as G9ByteSource).size === 'number' &&
    typeof (value as G9ByteSource).read === 'function'
  );
}

/** A {@link G9ByteSource} over bytes in memory. */
export function bytesSource(
  bytes: ArrayBuffer | ArrayBufferView,
  options: { name?: string; lastModified?: number } = {},
): G9ByteSource {
  const view = toUint8(bytes);
  return {
    size: view.length,
    name: options.name,
    lastModified: options.lastModified,
    read: (offset, length) => Promise.resolve(view.subarray(offset, Math.min(view.length, offset + length))),
  };
}

/**
 * Normalizes what the upload/hash APIs accept into a {@link G9ByteSource}: a source as is, a `Blob`/`File` read with
 * `slice().arrayBuffer()`, or bytes in memory.
 */
export function toByteSource(input: G9ByteSource | BlobLike | ArrayBuffer | ArrayBufferView): G9ByteSource {
  if (isByteSource(input)) return input;
  if (isBlobLike(input)) {
    const blob = input;
    return {
      size: blob.size,
      name: typeof blob.name === 'string' ? blob.name : undefined,
      lastModified: typeof blob.lastModified === 'number' ? blob.lastModified : undefined,
      read: (offset, length) => blob.slice(offset, Math.min(blob.size, offset + length)).arrayBuffer(),
    };
  }
  if (input instanceof ArrayBuffer || ArrayBuffer.isView(input)) return bytesSource(input);
  throw new TypeError('A Blob, File, G9ByteSource, ArrayBuffer or typed array is required.');
}

/** A Uint8Array view of `bytes` (no copy). */
export function toUint8(bytes: ArrayBuffer | ArrayBufferView): Uint8Array {
  if (bytes instanceof Uint8Array) return bytes;
  if (ArrayBuffer.isView(bytes)) return new Uint8Array(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  return new Uint8Array(bytes);
}
