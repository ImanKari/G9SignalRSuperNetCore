using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using G9SignalRSuperNetCore.Server.Classes.Errors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace G9SignalRSuperNetCore.Server.Classes.FileUpload;

/// <summary>System.Text.Json source-generated context so the upload metadata serializer is AOT-safe.</summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(G9CUploadService.G9CUploadMetadata))]
internal sealed partial class G9CUploadJsonContext : JsonSerializerContext { }

/// <summary>
///     Default file-upload service. Stores partials at <c>{Root}/{Partial}/{uploadId}.bin</c>
///     plus a sidecar <c>.meta</c> JSON file with the declared total size, file name, and
///     declared SHA-256. Writes are append-only and serialized per upload id.
/// </summary>
/// <remarks>
///     <para>The service is registered as a singleton through
///     <c>AddG9SignalRSuperNetCoreFileUpload</c>. It uses <see cref="ArrayPool{Byte}"/> for
///     hashing buffers and writes chunks straight to disk to keep allocations minimal.</para>
///     <para>Concurrency: a per-id <see cref="SemaphoreSlim"/> serializes writes so two
///     racing append calls for the same upload id never interleave bytes. The semaphore is
///     released and removed when the upload completes or fails.</para>
/// </remarks>
public sealed class G9CUploadService : IG9UploadService
{
    private const int MaxChunkSize = 4 * 1024 * 1024; // 4 MB hard cap for in-memory chunks.

    private readonly G9DtUploadOptions _options;
    private readonly ILogger<G9CUploadService> _log;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    /// <summary>Initializes the upload service.</summary>
    public G9CUploadService(IOptions<G9DtUploadOptions> options, ILogger<G9CUploadService> log)
    {
        _options = options.Value;
        _log = log;
        Directory.CreateDirectory(_options.RootDirectory);
        Directory.CreateDirectory(_options.PartialDirectory);
    }

    /// <inheritdoc />
    public async ValueTask<G9DtBeginUploadResult> BeginAsync(
        string uploadId,
        string fileName,
        long totalBytes,
        int chunkSize,
        string declaredSha256Hex,
        CancellationToken ct = default)
    {
        ValidateUploadId(uploadId);
        if (totalBytes <= 0) throw new ArgumentOutOfRangeException(nameof(totalBytes));
        if (totalBytes > _options.MaxBytes) throw new InvalidOperationException(G9CErrorCodes.UploadTooLarge);
        if (string.IsNullOrEmpty(fileName)) throw new ArgumentException("File name required.", nameof(fileName));
        if (string.IsNullOrEmpty(declaredSha256Hex) || declaredSha256Hex.Length != 64)
            throw new ArgumentException("SHA-256 (lower-case hex, 64 chars) required.", nameof(declaredSha256Hex));

        // Strip path components from the requested file name so a malicious client can't traverse.
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrEmpty(safeFileName)) safeFileName = uploadId;

        var capped = Math.Min(chunkSize, MaxChunkSize);
        if (capped <= 0) capped = 64 * 1024;

        var meta = new G9CUploadMetadata
        {
            UploadId = uploadId,
            FileName = safeFileName,
            TotalBytes = totalBytes,
            ChunkSize = capped,
            DeclaredSha256 = declaredSha256Hex.ToLowerInvariant(),
            CreatedUtc = DateTime.UtcNow
        };

        var partialPath = PartialPath(uploadId);
        var metaPath = MetaPath(uploadId);
        var finalPath = Path.Combine(_options.RootDirectory, safeFileName);

        // Idempotent: if already committed return that.
        if (File.Exists(finalPath) && !File.Exists(partialPath))
        {
            return new G9DtBeginUploadResult
            {
                UploadId = uploadId,
                BytesAlreadyReceived = totalBytes,
                ChunkSize = capped,
                AlreadyCompleted = true,
                FinalPath = finalPath
            };
        }

        var lockSlim = _locks.GetOrAdd(uploadId, static _ => new SemaphoreSlim(1, 1));
        await lockSlim.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Persist or refresh meta.
            await File.WriteAllTextAsync(metaPath,
                JsonSerializer.Serialize(meta, G9CUploadJsonContext.Default.G9CUploadMetadata),
                ct).ConfigureAwait(false);

            long alreadyReceived = 0;
            if (File.Exists(partialPath))
            {
                alreadyReceived = new FileInfo(partialPath).Length;
                if (alreadyReceived > totalBytes)
                {
                    // Partial is bigger than declared total — it's stale, reset it.
                    File.Delete(partialPath);
                    alreadyReceived = 0;
                }
            }

            return new G9DtBeginUploadResult
            {
                UploadId = uploadId,
                BytesAlreadyReceived = alreadyReceived,
                ChunkSize = capped,
                AlreadyCompleted = false
            };
        }
        finally
        {
            lockSlim.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<G9DtUploadResult> AppendChunksAsync(
        string uploadId,
        IAsyncEnumerable<byte[]> chunks,
        Func<long, ValueTask>? onProgress,
        CancellationToken ct = default)
    {
        ValidateUploadId(uploadId);

        var metaPath = MetaPath(uploadId);
        if (!File.Exists(metaPath))
            return Failure(G9CErrorCodes.UploadUnknownId, "Upload metadata not found. Call BeginAsync first.", 0);

        G9CUploadMetadata meta;
        try
        {
            meta = JsonSerializer.Deserialize(File.ReadAllText(metaPath),
                       G9CUploadJsonContext.Default.G9CUploadMetadata)
                   ?? throw new InvalidOperationException("Empty meta.");
        }
        catch (Exception ex)
        {
            return Failure(G9CErrorCodes.UploadUnknownId, "Corrupt upload metadata: " + ex.Message, 0);
        }

        var partialPath = PartialPath(uploadId);
        var lockSlim = _locks.GetOrAdd(uploadId, static _ => new SemaphoreSlim(1, 1));
        await lockSlim.WaitAsync(ct).ConfigureAwait(false);

        long bytesWrittenTotal = 0;
        var ackInterval = _options.AckEveryNChunks > 0 ? _options.AckEveryNChunks : int.MaxValue;
        var chunkCounter = 0;

        try
        {
            // Open the partial in append mode.
            await using (var fs = new FileStream(
                partialPath,
                new FileStreamOptions
                {
                    Mode = FileMode.OpenOrCreate,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.SequentialScan | FileOptions.Asynchronous
                }))
            {
                fs.Seek(0, SeekOrigin.End);
                bytesWrittenTotal = fs.Length;

                await foreach (var chunk in chunks.WithCancellation(ct).ConfigureAwait(false))
                {
                    if (chunk.Length == 0) continue;

                    if (bytesWrittenTotal + chunk.Length > meta.TotalBytes)
                    {
                        return Failure(G9CErrorCodes.UploadTooLarge,
                            $"Chunk would exceed declared total {meta.TotalBytes} bytes.",
                            bytesWrittenTotal);
                    }

                    await fs.WriteAsync(chunk.AsMemory(), ct).ConfigureAwait(false);
                    bytesWrittenTotal += chunk.Length;
                    chunkCounter++;

                    if (onProgress is not null && (chunkCounter % ackInterval == 0))
                        await onProgress(bytesWrittenTotal).ConfigureAwait(false);
                }

                await fs.FlushAsync(ct).ConfigureAwait(false);
            }

            // Final progress beat at completion / interruption.
            if (onProgress is not null) await onProgress(bytesWrittenTotal).ConfigureAwait(false);

            if (bytesWrittenTotal < meta.TotalBytes)
            {
                // Stream ended early but the partial is intact for resume.
                return new G9DtUploadResult
                {
                    Status = G9EUploadStatus.Interrupted,
                    BytesWritten = bytesWrittenTotal
                };
            }

            // Verify and commit.
            var actualSha = await ComputeSha256Async(partialPath, ct).ConfigureAwait(false);
            if (!string.Equals(actualSha, meta.DeclaredSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(partialPath);
                return Failure(G9CErrorCodes.UploadHashMismatch,
                    $"Declared SHA-256 {meta.DeclaredSha256} != actual {actualSha}.",
                    bytesWrittenTotal);
            }

            // Atomic rename. If a file already exists at the target name, append a timestamp.
            var finalPath = Path.Combine(_options.RootDirectory, meta.FileName);
            if (File.Exists(finalPath))
            {
                var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
                var ext = Path.GetExtension(meta.FileName);
                var stem = Path.GetFileNameWithoutExtension(meta.FileName);
                finalPath = Path.Combine(_options.RootDirectory, $"{stem}.{stamp}{ext}");
            }

            File.Move(partialPath, finalPath);
            File.Delete(metaPath);

            return new G9DtUploadResult
            {
                Status = G9EUploadStatus.Completed,
                BytesWritten = bytesWrittenTotal,
                Sha256 = actualSha,
                FinalPath = finalPath
            };
        }
        catch (OperationCanceledException)
        {
            // Caller cancelled; partial is preserved for resume.
            return new G9DtUploadResult { Status = G9EUploadStatus.Interrupted, BytesWritten = bytesWrittenTotal };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Upload {UploadId} failed", uploadId);
            return Failure(G9CErrorCodes.UploadFailed, ex.Message, bytesWrittenTotal);
        }
        finally
        {
            lockSlim.Release();
        }
    }

    /// <inheritdoc />
    public int CleanupExpiredPartials()
    {
        if (!Directory.Exists(_options.PartialDirectory)) return 0;
        var cutoff = DateTime.UtcNow - _options.PartialTtl;
        var removed = 0;

        foreach (var file in Directory.EnumerateFiles(_options.PartialDirectory))
        {
            try
            {
                var info = new FileInfo(file);
                if (info.LastWriteTimeUtc < cutoff)
                {
                    info.Delete();
                    removed++;
                }
            }
            catch
            {
                // best-effort cleanup; ignore individual failures.
            }
        }

        return removed;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1024 * 1024, FileOptions.SequentialScan | FileOptions.Asynchronous);
        using var sha = SHA256.Create();
        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(1024 * 1024);
        try
        {
            int read;
            while ((read = await fs.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
                sha.TransformBlock(buffer, 0, read, null, 0);
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        }
        finally
        {
            pool.Return(buffer);
        }
    }

    private static G9DtUploadResult Failure(string code, string message, long written) => new()
    {
        Status = G9EUploadStatus.Failed,
        BytesWritten = written,
        ErrorCode = code,
        ErrorMessage = message
    };

    private string PartialPath(string uploadId) => Path.Combine(_options.PartialDirectory, uploadId + ".bin");
    private string MetaPath(string uploadId) => Path.Combine(_options.PartialDirectory, uploadId + ".meta");

    private static void ValidateUploadId(string uploadId)
    {
        if (string.IsNullOrEmpty(uploadId)) throw new ArgumentException("Upload id required.", nameof(uploadId));
        // Allow only safe filename chars; the upload id is used in path construction.
        foreach (var c in uploadId)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_') continue;
            throw new ArgumentException("Upload id may only contain letters, digits, '-' and '_'.", nameof(uploadId));
        }
    }

    internal sealed class G9CUploadMetadata
    {
        public required string UploadId { get; init; }
        public required string FileName { get; init; }
        public required long TotalBytes { get; init; }
        public required int ChunkSize { get; init; }
        public required string DeclaredSha256 { get; init; }
        public required DateTime CreatedUtc { get; init; }
    }
}
