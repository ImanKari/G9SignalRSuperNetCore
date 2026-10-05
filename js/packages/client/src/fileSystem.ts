import type { G9ByteSource } from './byteSource.js';

/** What {@link G9FileSystem.stat} reports about an existing file. */
export interface G9FileStat {
  /** Size in bytes. */
  readonly size: number;
  /** The absolute, normalized path (the .NET `FileInfo.FullName`). */
  readonly fullPath: string;
  /**
   * Last write time in .NET ticks (100 ns since 0001-01-01 UTC, `FileInfo.LastWriteTimeUtc.Ticks`) as a decimal string:
   * the value exceeds 2^53, and Lynx has no BigInt.
   */
  readonly lastWriteTicks: string;
}

/**
 * The file operations the transfer classes need to work with files on disk the way the .NET client does
 * (`G9CFileUploader.UploadAsync(filePath)`, `G9CFileDownloader.DownloadAsync(serverFileName, localTargetPath)` with its
 * `.partial` file). Implementations: `nodeFileSystem` (`@g9tm/signalr-supernetcore-client/node`) and the Lynx native
 * module (`lynxFileSystem()` in `@g9tm/signalr-supernetcore-lynx`). Browsers have no file system: use Blobs there.
 */
export interface G9FileSystem {
  /** Facts about the file, or `null` when it does not exist. */
  stat(path: string): Promise<G9FileStat | null>;
  /** Reads `length` bytes from `offset` (fewer only at the end of the file). */
  read(path: string, offset: number, length: number): Promise<Uint8Array | ArrayBuffer>;
  /**
   * Writes `bytes` at `offset`, creating the file and its parent directories when missing and truncating the file to
   * `offset + bytes.length` when `truncate` is set (a resume that drops a stale tail).
   */
  write(path: string, offset: number, bytes: Uint8Array, truncate?: boolean): Promise<void>;
  /** Renames `from` to `to` (both on the same volume). Fails when `to` exists. */
  move(from: string, to: string): Promise<void>;
  /** Deletes the file; no error when it does not exist. */
  remove(path: string): Promise<void>;
}

/**
 * A {@link G9ByteSource} over a file: its size and identity are read once (the upload id key is the .NET one,
 * `lower(fullPath)|length|lastWriteTicks`), its bytes on demand.
 */
export async function fileByteSource(fileSystem: G9FileSystem, path: string): Promise<G9ByteSource> {
  const stat = await fileSystem.stat(path);
  if (!stat) {
    const error = new Error(`Upload source not found: ${path}`);
    error.name = 'NotFoundError';
    throw error;
  }
  return {
    size: stat.size,
    name: baseName(stat.fullPath),
    uploadKey: `${stat.fullPath.toLowerCase()}|${stat.size}|${stat.lastWriteTicks}`,
    read: (offset, length) => fileSystem.read(path, offset, length),
  };
}

/** The file name of a path (either separator). */
export function baseName(path: string): string {
  const cut = Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\'));
  return cut >= 0 ? path.slice(cut + 1) : path;
}

/** The directory of a path (either separator), '' when it has none. */
export function dirName(path: string): string {
  const cut = Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\'));
  return cut >= 0 ? path.slice(0, cut) : '';
}
