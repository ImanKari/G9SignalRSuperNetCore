using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.SignalR.Client;

namespace G9SignalRSuperNetCore.Client.FileUpload;

/// <summary>
///     Client-side resumable downloader. Uses SignalR's documented server-to-client streaming
///     pattern (<c>HubConnection.StreamAsync&lt;byte[]&gt;</c>) to fetch chunks from the server
///     and append them to a local <c>.partial</c> file. On completion the file is verified
///     against the server-reported SHA-256 and atomically renamed.
/// </summary>
/// <remarks>
///     <para>Resume: when the server pushes the chunk stream and the client connection drops or
///     the user cancels, the local <c>.partial</c> file is preserved and a subsequent
///     <see cref="DownloadAsync"/> call resumes from the partial's current length.</para>
///     <para>Verification: the server returns its SHA-256 in the begin handshake. The client
///     re-hashes the assembled file before committing and fails the download with
///     <c>G9_DOWNLOAD_HASH_MISMATCH</c> if it differs.</para>
///     <para>Allocations: a single rented <see cref="ArrayPool{T}.Shared"/> buffer for hashing.
///     Chunk arrays are produced by SignalR; we pass them straight to the file stream and
///     immediately discard.</para>
/// </remarks>
public sealed class G9CFileDownloader
{
    private const string ErrDownloadHashMismatch = "G9_DOWNLOAD_HASH_MISMATCH";
    private const string ErrDownloadNotFound = "G9_DOWNLOAD_NOT_FOUND";
    private const string ErrDownloadFailed = "G9_DOWNLOAD_FAILED";

    private readonly HubConnection _connection;
    private readonly G9DtDownloadClientOptions _options;

    /// <summary>Initializes a new downloader bound to <paramref name="connection"/>.</summary>
    public G9CFileDownloader(HubConnection connection, G9DtDownloadClientOptions? options = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options ?? new G9DtDownloadClientOptions();
    }

    /// <summary>
    ///     Downloads <paramref name="serverFileName"/> from the server into
    ///     <paramref name="localTargetPath"/>, resuming any partial that was preserved from a
    ///     previous attempt.
    /// </summary>
    /// <param name="serverFileName">Name of the file on the server (no path, no <c>..</c>).</param>
    /// <param name="localTargetPath">Absolute path where the downloaded file should land.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<G9DtDownloadResult> DownloadAsync(
        string serverFileName,
        string localTargetPath,
        IProgress<G9DtDownloadClientProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(serverFileName);
        ArgumentException.ThrowIfNullOrEmpty(localTargetPath);

        var partialPath = localTargetPath + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(localTargetPath)!);

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var existingPartial = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;

            G9DtBeginDownloadResult begin;
            try
            {
                begin = await _connection.InvokeAsync<G9DtBeginDownloadResult>(
                    _options.BeginMethod,
                    serverFileName, existingPartial, _options.ChunkSize,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < _options.MaxRetries)
            {
                ReportRetry(attempt, ex, existingPartial);
                await BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (begin.NotFound)
                return Failure(ErrDownloadNotFound, $"Server file '{serverFileName}' not found.", existingPartial);

            // Idempotent fast path: if a fully-downloaded committed file already matches the
            // server's hash, return Completed without re-streaming.
            if (_options.ShortCircuitWhenComplete && File.Exists(localTargetPath))
            {
                var existingSha = await ComputeSha256HexAsync(localTargetPath, cancellationToken).ConfigureAwait(false);
                if (string.Equals(existingSha, begin.Sha256, StringComparison.OrdinalIgnoreCase))
                    return new G9DtDownloadResult
                    {
                        Status = G9EUploadStatus.Completed,
                        BytesWritten = begin.TotalBytes,
                        LocalPath = localTargetPath,
                        Sha256 = begin.Sha256
                    };
            }

            // Stale partial larger than the server's file? Wipe and start over.
            if (existingPartial > begin.TotalBytes)
            {
                try { File.Delete(partialPath); } catch { /* ignore */ }
                existingPartial = 0;
            }

            var chunkSize = begin.ChunkSize > 0 ? begin.ChunkSize : _options.ChunkSize;
            var sw = Stopwatch.StartNew();
            long bytesWritten = existingPartial;

            try
            {
                await using (var fs = new FileStream(partialPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None,
                    bufferSize: 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    fs.Seek(existingPartial, SeekOrigin.Begin);

                    var stream = _connection.StreamAsync<byte[]>(
                        _options.StreamMethod,
                        serverFileName, existingPartial, chunkSize,
                        cancellationToken);

                    await foreach (var chunk in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
                    {
                        if (chunk.Length == 0) continue;
                        await fs.WriteAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
                        bytesWritten += chunk.Length;

                        progress?.Report(new G9DtDownloadClientProgress(
                            serverFileName,
                            bytesWritten,
                            begin.TotalBytes,
                            sw.Elapsed.TotalSeconds > 0 ? (bytesWritten - existingPartial) / sw.Elapsed.TotalSeconds : 0,
                            sw.Elapsed));
                    }

                    await fs.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new G9DtDownloadResult { Status = G9EUploadStatus.Interrupted, BytesWritten = bytesWritten };
            }
            catch (Exception ex) when (attempt < _options.MaxRetries)
            {
                ReportRetry(attempt, ex, bytesWritten);
                await BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (Exception ex)
            {
                return Failure(ErrDownloadFailed, ex.Message, bytesWritten);
            }

            // The stream ended early — the server hung up or the connection blipped.
            // Preserve the partial so the next call resumes from where we left off.
            if (bytesWritten < begin.TotalBytes)
            {
                if (attempt < _options.MaxRetries)
                {
                    ReportRetry(attempt, null, bytesWritten, interrupted: true);
                    await BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                return new G9DtDownloadResult { Status = G9EUploadStatus.Interrupted, BytesWritten = bytesWritten };
            }

            // Verify SHA-256 then atomically commit.
            var actualSha = await ComputeSha256HexAsync(partialPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualSha, begin.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(partialPath); } catch { /* ignore */ }
                return Failure(ErrDownloadHashMismatch,
                    $"Server SHA-256 {begin.Sha256} != local {actualSha}.",
                    bytesWritten);
            }

            if (File.Exists(localTargetPath))
            {
                // Don't clobber an unrelated file — append a timestamp like the server does.
                var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
                var stem = Path.GetFileNameWithoutExtension(localTargetPath);
                var ext = Path.GetExtension(localTargetPath);
                var dir = Path.GetDirectoryName(localTargetPath)!;
                localTargetPath = Path.Combine(dir, $"{stem}.{stamp}{ext}");
            }
            File.Move(partialPath, localTargetPath);

            return new G9DtDownloadResult
            {
                Status = G9EUploadStatus.Completed,
                BytesWritten = bytesWritten,
                LocalPath = localTargetPath,
                Sha256 = actualSha
            };
        }
    }

    private void ReportRetry(int attempt, Exception? ex, long bytesAlready, bool interrupted = false)
    {
        if (_options.OnRetry is null) return;
        var backoff = ComputeBackoff(attempt);
        _options.OnRetry(new G9DtUploadRetryInfo(
            interrupted ? G9EUploadRetryReason.Interrupted : G9EUploadRetryReason.AttemptFailed,
            attempt, _options.MaxRetries, bytesAlready, backoff, ex));
    }

    private static TimeSpan ComputeBackoff(int attempt)
        => TimeSpan.FromMilliseconds(Math.Min(30_000, 200 * Math.Pow(2, attempt)));

    private static Task BackoffAsync(int attempt, CancellationToken ct)
        => Task.Delay(ComputeBackoff(attempt), ct);

    private static G9DtDownloadResult Failure(string code, string message, long written) => new()
    {
        Status = G9EUploadStatus.Failed,
        BytesWritten = written,
        ErrorCode = code,
        ErrorMessage = message
    };

    private static async Task<string> ComputeSha256HexAsync(string path, CancellationToken ct)
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
}
