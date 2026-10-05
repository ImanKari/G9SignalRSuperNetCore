import { fileByteSource, type G9ByteSource, type G9FileStat, type G9FileSystem } from '@g9tm/signalr-supernetcore-client';
import { exactBuffer, G9NativeModuleError, getG9SignalRNative, type G9SignalRNative } from './native.js';

function requireNative(native: G9SignalRNative | undefined): G9SignalRNative {
  const resolved = native ?? getG9SignalRNative();
  if (!resolved) {
    throw new Error('G9SignalRLynxModule is not available: files cannot be read or written without it.');
  }
  return resolved;
}

/**
 * {@link G9FileSystem} over the native module: lets `G9FileUploader.uploadFile(path)` and
 * `G9FileDownloader.downloadToFile(name, path)` work on Lynx exactly as `UploadAsync(filePath)` /
 * `DownloadAsync(name, localPath)` do in .NET (same upload id, same `.partial` resume). Paths are the platform's
 * absolute paths (Android `Context.filesDir`-based, iOS sandbox paths, desktop paths).
 */
export function lynxFileSystem(native?: G9SignalRNative): G9FileSystem {
  const module = (): G9SignalRNative => requireNative(native);
  return {
    async stat(path: string): Promise<G9FileStat | null> {
      const stat = await module().fileStat(path);
      return stat ? { size: stat.size, fullPath: stat.fullPath, lastWriteTicks: stat.lastWriteTicks } : null;
    },
    async read(path: string, offset: number, length: number): Promise<ArrayBuffer> {
      return module().fileRead(path, offset, length);
    },
    async write(path: string, offset: number, bytes: Uint8Array, truncate?: boolean): Promise<void> {
      await module().fileWrite(path, offset, exactBuffer(bytes), truncate === true);
    },
    async move(from: string, to: string): Promise<void> {
      await module().fileMove(from, to);
    },
    async remove(path: string): Promise<void> {
      try {
        await module().fileDelete(path);
      } catch (error) {
        if (!(error instanceof G9NativeModuleError && error.code === 'not-found')) throw error;
      }
    },
  };
}

/** A byte source over a file on disk, for `G9FileUploader.upload` (the upload id key is the .NET one). */
export function lynxFileSource(path: string, native?: G9SignalRNative): Promise<G9ByteSource> {
  return fileByteSource(lynxFileSystem(native), path);
}
