/// <reference types="node" />
// Node-only helpers (`@g9tm/signalr-supernetcore-client/node`): kept out of the main entry so browser and Lynx bundles
// never see `node:fs`.
import { mkdir, open, rename, stat, unlink } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import type { G9ByteSource } from '../byteSource.js';
import { fileByteSource, type G9FileStat, type G9FileSystem } from '../fileSystem.js';

/** .NET ticks at the Unix epoch (1970-01-01T00:00:00Z). */
const UNIX_EPOCH_TICKS = '621355968000000000';

/** Sum of two non-negative decimal integer strings (the package targets ES2019, which has no BigInt literals). */
export function addDecimal(a: string, b: string): string {
  let out = '';
  let carry = 0;
  for (let i = a.length - 1, j = b.length - 1; i >= 0 || j >= 0 || carry; i--, j--) {
    const sum = (i >= 0 ? a.charCodeAt(i) - 48 : 0) + (j >= 0 ? b.charCodeAt(j) - 48 : 0) + carry;
    out = String(sum % 10) + out;
    carry = sum >= 10 ? 1 : 0;
  }
  return out.replace(/^0+(?=\d)/, '');
}

/** .NET ticks of a nanosecond Unix time (`mtimeNs`), as a decimal string. */
export function unixNanosecondsToTicks(nanoseconds: string): string {
  const hundreds = nanoseconds.length > 2 ? nanoseconds.slice(0, -2) : '0';
  return addDecimal(UNIX_EPOCH_TICKS, hundreds);
}

function isMissing(error: unknown): boolean {
  return typeof error === 'object' && error !== null && (error as { code?: unknown }).code === 'ENOENT';
}

/**
 * {@link G9FileSystem} over `node:fs`. `stat` reports the .NET `FileInfo` facts (`FullName`, `Length`,
 * `LastWriteTimeUtc.Ticks` from the nanosecond mtime), so `G9FileUploader.uploadFile` computes the same upload id as
 * the .NET `G9CFileUploader.UploadAsync` for the same file.
 */
export const nodeFileSystem: G9FileSystem = {
  async stat(path: string): Promise<G9FileStat | null> {
    try {
      const info = await stat(path, { bigint: true });
      if (!info.isFile()) return null;
      return {
        size: Number(info.size),
        fullPath: resolve(path),
        lastWriteTicks: unixNanosecondsToTicks(info.mtimeNs.toString()),
      };
    } catch (error) {
      if (isMissing(error)) return null;
      throw error;
    }
  },

  async read(path: string, offset: number, length: number): Promise<Uint8Array> {
    const handle = await open(path, 'r');
    try {
      const buffer = new Uint8Array(length);
      let filled = 0;
      while (filled < length) {
        const { bytesRead } = await handle.read(buffer, filled, length - filled, offset + filled);
        if (bytesRead === 0) break;
        filled += bytesRead;
      }
      return filled === length ? buffer : buffer.subarray(0, filled);
    } finally {
      await handle.close();
    }
  },

  async write(path: string, offset: number, bytes: Uint8Array, truncate?: boolean): Promise<void> {
    await mkdir(dirname(resolve(path)), { recursive: true });
    let handle;
    try {
      handle = await open(path, 'r+');
    } catch (error) {
      if (!isMissing(error)) throw error;
      handle = await open(path, 'w+');
    }
    try {
      let written = 0;
      while (written < bytes.length) {
        const { bytesWritten } = await handle.write(bytes, written, bytes.length - written, offset + written);
        written += bytesWritten;
      }
      if (truncate) await handle.truncate(offset + bytes.length);
      await handle.sync();
    } finally {
      await handle.close();
    }
  },

  async move(from: string, to: string): Promise<void> {
    try {
      await stat(to);
      throw Object.assign(new Error(`The target file already exists: ${to}`), { code: 'EEXIST' });
    } catch (error) {
      if (!isMissing(error)) throw error;
    }
    await mkdir(dirname(resolve(to)), { recursive: true });
    await rename(from, to);
  },

  async remove(path: string): Promise<void> {
    try {
      await unlink(path);
    } catch (error) {
      if (!isMissing(error)) throw error;
    }
  },
};

/** A {@link G9ByteSource} over a file on disk (upload id key = the .NET one). */
export function nodeFileSource(path: string): Promise<G9ByteSource> {
  return fileByteSource(nodeFileSystem, path);
}
