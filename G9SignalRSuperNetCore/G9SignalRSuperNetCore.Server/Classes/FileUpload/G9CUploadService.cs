using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
///     Default file-upload service. Stores partials at <c>{Root}/{Partial}/{id}.bin</c>
///     plus a sidecar <c>.meta</c> JSON file with the declared total size, file name, and
///     declared SHA-256. Writes are append-only and serialized per upload id.
/// </summary>
/// <remarks>
///     <para>The service is registered as a singleton through
///     <c>AddG9SignalRSuperNetCoreFileUpload</c>. It uses <see cref="ArrayPool{Byte}"/> for
///     hashing buffers and writes chunks straight to disk to keep allocations minimal.</para>
///     <para>Concurrency: a per-id <see cref="SemaphoreSlim"/> serializes writes so two
///     racing append calls for the same upload id never interleave bytes.</para>
///     <para>2.9: the <c>{id}</c> of a partial is the upload id, or, with
///     <see cref="G9DtUploadOptions.PerUserNamespace"/> and an owner, <c>{owner}-{owner hash}__{uploadId}</c> where
///     <c>{owner}</c> is the owner id reduced to <c>[A-Za-z0-9-]</c> (at most 32 characters) and <c>{owner hash}</c> is
///     the first 16 hex characters of the SHA-256 of the exact owner id. The hash is what keeps owners apart: the
///     readable part alone would give <c>a.b</c> and <c>a-b</c> the same namespace.</para>
/// </remarks>
public sealed class G9CUploadService : IG9UploadService
{
    private const int MaxChunkSize = 4 * 1024 * 1024; // 4 MB hard cap for in-memory chunks.
    private const int MaxOwnerSegmentLength = 32;
    private const int MaxExtensionLength = 16;

    private readonly G9DtUploadOptions _options;
    private readonly ILogger<G9CUploadService> _log;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (long Length, DateTime LastWriteUtc, string Sha256)> _hashCache = new(StringComparer.Ordinal);

    /// <summary>Initializes the upload service.</summary>
    public G9CUploadService(IOptions<G9DtUploadOptions> options, ILogger<G9CUploadService> log)
    {
        _options = options.Value;
        _log = log;
        Directory.CreateDirectory(_options.RootDirectory);
        Directory.CreateDirectory(_options.PartialDirectory);
    }

    /// <inheritdoc />
    public ValueTask<G9DtBeginUploadResult> BeginAsync(
        string uploadId,
        string fileName,
        long totalBytes,
        int chunkSize,
        string declaredSha256Hex,
        CancellationToken ct = default)
        => BeginAsync(null, uploadId, fileName, totalBytes, chunkSize, declaredSha256Hex, ct);

    /// <inheritdoc />
    public async ValueTask<G9DtBeginUploadResult> BeginAsync(
        string? ownerId,
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

        // Strip path components from the requested file name so a malicious client cannot traverse.
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrEmpty(safeFileName)) safeFileName = uploadId;

        if (!await IsAllowedAsync(G9EUploadOperation.Begin, ownerId, uploadId, safeFileName, totalBytes, ct).ConfigureAwait(false))
            throw new InvalidOperationException(G9CErrorCodes.UploadForbidden);

        var capped = Math.Min(chunkSize, MaxChunkSize);
        if (capped <= 0) capped = 64 * 1024;

        var meta = new G9CUploadMetadata
        {
            UploadId = uploadId,
            OwnerId = ownerId,
            FileName = safeFileName,
            TotalBytes = totalBytes,
            ChunkSize = capped,
            DeclaredSha256 = declaredSha256Hex.ToLowerInvariant(),
            CreatedUtc = DateTime.UtcNow
        };

        var storageId = StorageId(ownerId, uploadId);
        var partialPath = PartialPath(storageId);
        var metaPath = MetaPath(storageId);
        var finalPath = Path.Combine(_options.RootDirectory, safeFileName);

        // Idempotent ONLY for the same content. A file at this path proves a file with this NAME was
        // committed; it says nothing about whose bytes they are. Reporting a different upload complete
        // because the name matched would tell the client its data is safe when it was never sent
        // (review T05), so the committed file has to match the size and hash being declared.
        // With randomized committed names (2.9) a name identifies nothing, so this fast path does not apply.
        if (!_options.RandomizeCommittedNames && File.Exists(finalPath) && !File.Exists(partialPath))
        {
            var committed = new FileInfo(finalPath);
            if (committed.Length == totalBytes &&
                string.Equals(await GetOrComputeShaAsync(finalPath, committed, ct).ConfigureAwait(false),
                    meta.DeclaredSha256, StringComparison.Ordinal))
            {
                return new G9DtBeginUploadResult
                {
                    UploadId = uploadId,
                    BytesAlreadyReceived = totalBytes,
                    ChunkSize = capped,
                    AlreadyCompleted = true,
                    FinalPath = finalPath,
                    StoredFileName = safeFileName
                };
            }

            _log.LogWarning(
                "Upload {UploadId} declares {DeclaredBytes} bytes for {FileName}, but a different file of {CommittedBytes} bytes is already committed under that name",
                uploadId, totalBytes, safeFileName, committed.Length);
            throw new InvalidOperationException(G9CErrorCodes.UploadNameConflict);
        }

        var lockSlim = _locks.GetOrAdd(storageId, static _ => new SemaphoreSlim(1, 1));
        await lockSlim.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // An upload id stands for one piece of content. Resuming it with a different size, name or
            // hash is a different upload wearing the same id: silently adopting the new numbers would
            // append the new bytes to the old partial and hash-check the result against neither (T05).
            if (File.Exists(metaPath))
            {
                var existing = JsonSerializer.Deserialize(
                    await File.ReadAllTextAsync(metaPath, ct).ConfigureAwait(false),
                    G9CUploadJsonContext.Default.G9CUploadMetadata);
                if (existing is not null && !existing.DescribesSameContentAs(meta))
                {
                    _log.LogWarning(
                        "Upload {UploadId} was begun for {OldFileName} ({OldBytes} bytes) and is being resumed as {NewFileName} ({NewBytes} bytes)",
                        uploadId, existing.FileName, existing.TotalBytes, meta.FileName, meta.TotalBytes);
                    throw new InvalidOperationException(G9CErrorCodes.UploadMetadataConflict);
                }
            }

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
    public ValueTask<G9DtUploadResult> AppendChunksAsync(
        string uploadId,
        IAsyncEnumerable<byte[]> chunks,
        Func<long, ValueTask>? onProgress,
        CancellationToken ct = default)
        => AppendChunksAsync(null, uploadId, chunks, onProgress, ct);

    /// <inheritdoc />
    public async ValueTask<G9DtUploadResult> AppendChunksAsync(
        string? ownerId,
        string uploadId,
        IAsyncEnumerable<byte[]> chunks,
        Func<long, ValueTask>? onProgress,
        CancellationToken ct = default)
    {
        ValidateUploadId(uploadId);

        var storageId = StorageId(ownerId, uploadId);
        var metaPath = MetaPath(storageId);
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

        if (!await IsAllowedAsync(G9EUploadOperation.Append, ownerId, uploadId, meta.FileName, meta.TotalBytes, ct).ConfigureAwait(false))
            return Failure(G9CErrorCodes.UploadForbidden, "The upload authorization hook refused this operation.", 0);

        var partialPath = PartialPath(storageId);
        var lockSlim = _locks.GetOrAdd(storageId, static _ => new SemaphoreSlim(1, 1));
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

            var finalPath = CommitPath(meta.FileName);
            File.Move(partialPath, finalPath);
            File.Delete(metaPath);

            return new G9DtUploadResult
            {
                Status = G9EUploadStatus.Completed,
                BytesWritten = bytesWrittenTotal,
                Sha256 = actualSha,
                FinalPath = finalPath,
                StoredFileName = Path.GetFileName(finalPath)
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
    /// <remarks>
    ///     2.9: a partial is its <c>.bin</c> plus its <c>.meta</c> sidecar, and the two are judged and removed
    ///     together: a partial expires when the most recent write to EITHER file is older than
    ///     <see cref="G9DtUploadOptions.PartialTtl"/>, and its <c>.meta</c> is deleted only once its <c>.bin</c> is
    ///     gone. (Before 2.9 each file was judged alone, so the metadata of a slow upload, written once at begin,
    ///     could be purged from under a <c>.bin</c> that was still growing, and an orphaned <c>.meta</c> could
    ///     outlive its data.) A partial whose upload is running right now is skipped.
    /// </remarks>
    public int CleanupExpiredPartials()
    {
        if (!Directory.Exists(_options.PartialDirectory)) return 0;
        var cutoff = DateTime.UtcNow - _options.PartialTtl;
        var removed = 0;

        // Group files by partial id; anything that is neither .bin nor .meta is judged on its own, as before.
        var partials = new Dictionary<string, PartialFiles>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(_options.PartialDirectory))
        {
            DateTime written;
            try
            {
                written = File.GetLastWriteTimeUtc(file);
            }
            catch
            {
                continue; // vanished or unreadable; best effort
            }

            var extension = Path.GetExtension(file);
            var isBin = extension.Equals(".bin", StringComparison.OrdinalIgnoreCase);
            var isMeta = extension.Equals(".meta", StringComparison.OrdinalIgnoreCase);
            var key = isBin || isMeta ? Path.GetFileNameWithoutExtension(file) : "\0" + file;

            if (!partials.TryGetValue(key, out var group)) partials[key] = group = new PartialFiles();
            if (isMeta) group.Meta = file;
            else group.Data = file;
            if (written > group.LastWriteUtc) group.LastWriteUtc = written;
        }

        foreach (var (id, group) in partials)
        {
            if (group.LastWriteUtc >= cutoff) continue;

            // Never pull a partial out from under an upload that is writing it right now.
            SemaphoreSlim? gate = null;
            if (_locks.TryGetValue(id, out var existingGate))
            {
                if (!existingGate.Wait(0)) continue;
                gate = existingGate;
            }

            try
            {
                if (group.Data is not null)
                {
                    try
                    {
                        File.Delete(group.Data);
                        removed++;
                    }
                    catch
                    {
                        continue; // keep the metadata of data we could not delete, so the pair stays consistent
                    }
                }

                if (group.Meta is not null)
                {
                    try
                    {
                        File.Delete(group.Meta);
                        removed++;
                    }
                    catch
                    {
                        // best-effort cleanup; ignore individual failures.
                    }
                }
            }
            finally
            {
                gate?.Release();
            }
        }

        return removed;
    }

    /// <inheritdoc />
    public ValueTask<G9DtBeginDownloadResult> BeginDownloadAsync(
        string serverRelativePath,
        long resumeFrom,
        int chunkSize,
        CancellationToken ct = default)
        => BeginDownloadAsync(null, serverRelativePath, resumeFrom, chunkSize, ct);

    /// <inheritdoc />
    public async ValueTask<G9DtBeginDownloadResult> BeginDownloadAsync(
        string? ownerId,
        string serverRelativePath,
        long resumeFrom,
        int chunkSize,
        CancellationToken ct = default)
    {
        var resolved = ResolveDownloadPath(serverRelativePath);
        var info = new FileInfo(resolved);

        // Authorize before revealing whether the file exists.
        if (!await IsAllowedAsync(G9EUploadOperation.BeginDownload, ownerId, string.Empty, info.Name,
                info.Exists ? info.Length : 0, ct).ConfigureAwait(false))
            throw new InvalidOperationException(G9CErrorCodes.UploadForbidden);

        if (!info.Exists)
            return new G9DtBeginDownloadResult { NotFound = true };

        var sha = await GetOrComputeShaAsync(resolved, info, ct).ConfigureAwait(false);

        var capped = Math.Min(chunkSize, MaxChunkSize);
        if (capped <= 0) capped = 64 * 1024;

        return new G9DtBeginDownloadResult
        {
            TotalBytes = info.Length,
            Sha256 = sha,
            ChunkSize = capped,
            ResumeFrom = Math.Clamp(resumeFrom, 0, info.Length)
        };
    }

    /// <inheritdoc />
    public IAsyncEnumerable<byte[]> StreamFileAsync(
        string serverRelativePath,
        long resumeFrom,
        int chunkSize,
        CancellationToken ct = default)
        => StreamFileAsync(null, serverRelativePath, resumeFrom, chunkSize, ct);

    /// <inheritdoc />
    public async IAsyncEnumerable<byte[]> StreamFileAsync(
        string? ownerId,
        string serverRelativePath,
        long resumeFrom,
        int chunkSize,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var resolved = ResolveDownloadPath(serverRelativePath);
        var info = new FileInfo(resolved);

        if (!await IsAllowedAsync(G9EUploadOperation.Download, ownerId, string.Empty, info.Name,
                info.Exists ? info.Length : 0, ct).ConfigureAwait(false))
            throw new InvalidOperationException(G9CErrorCodes.UploadForbidden);

        if (!info.Exists)
            throw new FileNotFoundException("Server file not found.", serverRelativePath);

        var capped = Math.Clamp(chunkSize, 1024, MaxChunkSize);
        var startOffset = Math.Clamp(resumeFrom, 0, info.Length);

        await using var fs = new FileStream(resolved, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1024 * 1024, FileOptions.SequentialScan | FileOptions.Asynchronous);
        if (startOffset > 0) fs.Seek(startOffset, SeekOrigin.Begin);

        var pool = ArrayPool<byte>.Shared;
        var rented = pool.Rent(capped);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var read = await fs.ReadAsync(rented.AsMemory(0, capped), ct).ConfigureAwait(false);
                if (read == 0) yield break;

                // Allocate a precisely-sized array because SignalR's JSON HubProtocol base64-encodes
                // byte[] and a pooled buffer would race with the serializer.
                var chunk = new byte[read];
                Buffer.BlockCopy(rented, 0, chunk, 0, read);
                yield return chunk;
            }
        }
        finally
        {
            pool.Return(rented);
        }
    }

    /// <summary>
    ///     Where a verified upload is committed: a random <c>{32 hex}{extension}</c> name with
    ///     <see cref="G9DtUploadOptions.RandomizeCommittedNames"/> (2.9), otherwise the declared name, timestamp-suffixed
    ///     when that name is taken.
    /// </summary>
    private string CommitPath(string declaredFileName)
    {
        string finalPath;
        if (_options.RandomizeCommittedNames)
        {
            var extension = SafeExtension(declaredFileName);
            do
            {
                finalPath = Path.Combine(_options.RootDirectory, Guid.NewGuid().ToString("N") + extension);
            } while (File.Exists(finalPath));

            return finalPath;
        }

        // If a file already exists at the target name, append a timestamp.
        finalPath = Path.Combine(_options.RootDirectory, declaredFileName);
        if (File.Exists(finalPath))
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
            var ext = Path.GetExtension(declaredFileName);
            var stem = Path.GetFileNameWithoutExtension(declaredFileName);
            finalPath = Path.Combine(_options.RootDirectory, $"{stem}.{stamp}{ext}");
        }

        return finalPath;
    }

    /// <summary>The declared extension, lower-cased, reduced to <c>[a-z0-9.]</c> and at most 16 characters; empty when nothing usable is left.</summary>
    private static string SafeExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension)) return string.Empty;

        var safe = new StringBuilder(MaxExtensionLength);
        foreach (var c in extension.ToLowerInvariant())
        {
            if (safe.Length == MaxExtensionLength) break;
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.') safe.Append(c);
        }

        return safe.Length > 1 && safe[0] == '.' ? safe.ToString() : string.Empty;
    }

    /// <summary>
    ///     The id a partial and its metadata are stored under: the upload id, or with
    ///     <see cref="G9DtUploadOptions.PerUserNamespace"/> and a non-empty owner, <c>{owner}-{owner hash}__{uploadId}</c>.
    /// </summary>
    private string StorageId(string? ownerId, string uploadId)
    {
        if (!_options.PerUserNamespace || string.IsNullOrEmpty(ownerId)) return uploadId;

        // Readable part: [A-Za-z0-9-] only. '_' is excluded so the "__" separator can only be the separator.
        var readable = new StringBuilder(MaxOwnerSegmentLength + 18);
        foreach (var c in ownerId)
        {
            if (readable.Length == MaxOwnerSegmentLength) break;
            readable.Append(char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '-');
        }

        // Identity part: the readable part is lossy (and truncated), so a hash of the exact owner id keeps owners apart.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(ownerId));
        readable.Append('-').Append(Convert.ToHexString(hash, 0, 8).ToLowerInvariant());
        return readable.Append("__").Append(uploadId).ToString();
    }

    /// <summary>Asks <see cref="G9DtUploadOptions.Authorize"/>, when set; everything is allowed without it.</summary>
    private async ValueTask<bool> IsAllowedAsync(
        G9EUploadOperation operation, string? ownerId, string uploadId, string fileName, long totalBytes, CancellationToken ct)
    {
        var authorize = _options.Authorize;
        if (authorize is null) return true;

        var allowed = await authorize(new G9DtUploadAuthorizationContext(operation, ownerId, uploadId, fileName, totalBytes), ct)
            .ConfigureAwait(false);
        if (!allowed)
            _log.LogWarning("Upload authorization refused {Operation} of {FileName} (upload {UploadId}) for owner {OwnerId}",
                operation, fileName, uploadId, ownerId);
        return allowed;
    }

    private string ResolveDownloadPath(string serverRelativePath)
    {
        if (string.IsNullOrEmpty(serverRelativePath))
            throw new ArgumentException("Server file path required.", nameof(serverRelativePath));

        // Normalize to defeat .. traversal and absolute-path tricks.
        var safe = Path.GetFileName(serverRelativePath);
        if (string.IsNullOrEmpty(safe))
            throw new ArgumentException("Server file path must be a simple file name.", nameof(serverRelativePath));

        var fullRoot = Path.GetFullPath(_options.RootDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, safe));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path traversal denied.");
        return fullPath;
    }

    private async Task<string> GetOrComputeShaAsync(string path, FileInfo info, CancellationToken ct)
    {
        if (_hashCache.TryGetValue(path, out var entry)
            && entry.Length == info.Length
            && entry.LastWriteUtc == info.LastWriteTimeUtc)
        {
            return entry.Sha256;
        }

        var sha = await ComputeSha256Async(path, ct).ConfigureAwait(false);
        _hashCache[path] = (info.Length, info.LastWriteTimeUtc, sha);
        return sha;
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

    private string PartialPath(string storageId) => Path.Combine(_options.PartialDirectory, storageId + ".bin");
    private string MetaPath(string storageId) => Path.Combine(_options.PartialDirectory, storageId + ".meta");

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

    /// <summary>The files of one partial as the cleanup found them.</summary>
    private sealed class PartialFiles
    {
        public string? Data { get; set; }
        public string? Meta { get; set; }
        public DateTime LastWriteUtc { get; set; } = DateTime.MinValue;
    }

    internal sealed class G9CUploadMetadata
    {
        public required string UploadId { get; init; }

        /// <summary>The owner the upload was begun for (2.9); absent in metadata written by earlier versions.</summary>
        public string? OwnerId { get; init; }

        public required string FileName { get; init; }
        public required long TotalBytes { get; init; }
        public required int ChunkSize { get; init; }
        public required string DeclaredSha256 { get; init; }
        public required DateTime CreatedUtc { get; init; }

        /// <summary>
        ///     Whether two begin calls for one upload id describe the same content. The chunk size is
        ///     deliberately excluded: a client may resume with a different chunk size, and that changes
        ///     only how the same bytes are carried.
        /// </summary>
        public bool DescribesSameContentAs(G9CUploadMetadata other) =>
            TotalBytes == other.TotalBytes
            && string.Equals(FileName, other.FileName, StringComparison.Ordinal)
            && string.Equals(DeclaredSha256, other.DeclaredSha256, StringComparison.Ordinal);
    }
}
