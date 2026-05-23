using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.AspNetCore.SignalR.Client;

namespace G9SignalRSuperNetCore.Client.FileUpload;

/// <summary>
///     Client-side resumable file uploader. Builds a deterministic upload id from the file path,
///     length, and last-write timestamp, calls the server's <c>BeginUpload</c> hub method to
///     learn how many bytes are already on the server, then streams the remainder via SignalR
///     client-to-server streaming (the documented <c>HubConnection.InvokeAsync</c> +
///     <see cref="IAsyncEnumerable{T}"/> pattern).
/// </summary>
/// <remarks>
///     <para>Progress: the caller's <see cref="IProgress{T}"/> sink receives both
///     locally-counted bytes (<see cref="G9DtUploadClientProgress.BytesSent"/>) and
///     server-acknowledged bytes (<see cref="G9DtUploadClientProgress.BytesAcknowledged"/>),
///     plus throughput (bytes/sec) and elapsed time.</para>
///     <para>Resume: when an upload is interrupted (cancellation, disconnect, transient I/O
///     error), call <see cref="UploadAsync"/> again with the same file path. The uploader
///     re-runs <c>BeginUpload</c>, the server returns <c>BytesAlreadyReceived</c>, and the
///     client seeks to that offset before continuing.</para>
///     <para>Allocations: a single rented buffer from <see cref="ArrayPool{T}.Shared"/> is
///     reused for disk reads. Each chunk allocates one fresh sized array because SignalR's
///     JSON HubProtocol base64-encodes the array and a pooled buffer would race with the
///     serializer.</para>
///     <para>Wire-size budget: SignalR enforces
///     <c>HubOptions.MaximumReceiveMessageSize</c>
///     (32 KB by default). The G9 server raises that to 4 MB so chunks up to ~3 MB are safe
///     even after JSON+base64 expansion. Match the chunk size to your latency/throughput
///     budget; 64 KB is a balanced default.</para>
/// </remarks>
public sealed class G9CFileUploader
{
    private readonly HubConnection _connection;
    private readonly G9DtUploadClientOptions _options;

    /// <summary>Initializes a new uploader bound to <paramref name="connection"/>.</summary>
    public G9CFileUploader(HubConnection connection, G9DtUploadClientOptions? options = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options ?? new G9DtUploadClientOptions();
    }

    /// <summary>
    ///     Uploads <paramref name="filePath"/> to the server, automatically resuming if a partial
    ///     upload exists for the same content.
    /// </summary>
    /// <param name="filePath">Local file path to upload.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="serverAckProgress">When set, listens for the server's
    /// <c>UploadProgress(G9DtUploadProgress)</c> callback and merges the acknowledged byte
    /// count into <paramref name="progress"/>.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The final upload result.</returns>
    public async Task<G9DtUploadResult> UploadAsync(
        string filePath,
        IProgress<G9DtUploadClientProgress>? progress = null,
        bool serverAckProgress = true,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("Upload source not found.", filePath);

        var fileInfo = new FileInfo(filePath);
        var totalBytes = fileInfo.Length;
        if (totalBytes == 0) throw new InvalidOperationException("Refusing to upload a zero-byte file.");

        var uploadId = ComputeUploadId(fileInfo);
        var declaredSha = await ComputeSha256HexAsync(fileInfo, cancellationToken).ConfigureAwait(false);

        long ackedBytes = 0;
        IDisposable? ackSubscription = null;
        if (serverAckProgress)
        {
            ackSubscription = _connection.On<G9DtUploadProgress>("UploadProgress", p =>
            {
                if (string.Equals(p.UploadId, uploadId, StringComparison.Ordinal))
                    Interlocked.Exchange(ref ackedBytes, p.BytesReceived);
                return Task.CompletedTask;
            });
        }

        try
        {
            Exception? lastError = null;

            for (var attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // BeginUpload returns the server-side offset to resume from (or AlreadyCompleted=true).
                var begin = await _connection.InvokeAsync<G9DtBeginUploadResult>(
                    _options.BeginMethod,
                    uploadId, fileInfo.Name, totalBytes, _options.ChunkSize, declaredSha,
                    cancellationToken).ConfigureAwait(false);

                if (begin.AlreadyCompleted)
                {
                    return new G9DtUploadResult
                    {
                        Status = G9EUploadStatus.Completed,
                        BytesWritten = totalBytes,
                        Sha256 = declaredSha,
                        FinalPath = begin.FinalPath
                    };
                }

                var startOffset = begin.BytesAlreadyReceived;
                var chunkSize = begin.ChunkSize > 0 ? begin.ChunkSize : _options.ChunkSize;

                progress?.Report(new G9DtUploadClientProgress(
                    uploadId, startOffset, Interlocked.Read(ref ackedBytes),
                    totalBytes, 0, TimeSpan.Zero));

                G9DtUploadResult result;
                try
                {
                    result = await StreamRemainingAsync(uploadId, fileInfo, totalBytes, startOffset, chunkSize,
                        progress, () => Interlocked.Read(ref ackedBytes), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (attempt >= _options.MaxRetries)
                        throw; // give up — let the caller see the real exception.

                    var backoff = TimeSpan.FromMilliseconds(Math.Min(30_000, 200 * Math.Pow(2, attempt)));
                    _options.OnRetry?.Invoke(new G9DtUploadRetryInfo(
                        G9EUploadRetryReason.AttemptFailed, attempt, _options.MaxRetries,
                        startOffset, backoff, ex));
                    await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (result.Status == G9EUploadStatus.Interrupted && attempt < _options.MaxRetries)
                {
                    var backoff = TimeSpan.FromMilliseconds(Math.Min(30_000, 200 * Math.Pow(2, attempt)));
                    _options.OnRetry?.Invoke(new G9DtUploadRetryInfo(
                        G9EUploadRetryReason.Interrupted, attempt, _options.MaxRetries,
                        result.BytesWritten, backoff, null));
                    await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                _ = lastError; // last error preserved on object graph if needed by future logging.
                return result;
            }
        }
        finally
        {
            ackSubscription?.Dispose();
        }
    }

    /// <summary>
    ///     Streams the remaining bytes (from <paramref name="startOffset"/> to
    ///     <paramref name="totalBytes"/>) using the documented client→server streaming pattern:
    ///     pass an <see cref="IAsyncEnumerable{T}"/> of <see cref="byte"/>[] as the second hub
    ///     argument. SignalR finishes the stream when the local async iterator exits.
    /// </summary>
    private async Task<G9DtUploadResult> StreamRemainingAsync(
        string uploadId,
        FileInfo fileInfo,
        long totalBytes,
        long startOffset,
        int chunkSize,
        IProgress<G9DtUploadClientProgress>? progress,
        Func<long> readAcked,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        async IAsyncEnumerable<byte[]> ProduceChunks([EnumeratorCancellation] CancellationToken token)
        {
            await using var fs = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1024 * 1024, FileOptions.SequentialScan | FileOptions.Asynchronous);
            fs.Seek(startOffset, SeekOrigin.Begin);

            var pool = ArrayPool<byte>.Shared;
            var rented = pool.Rent(chunkSize);
            try
            {
                long sent = startOffset;
                while (sent < totalBytes)
                {
                    token.ThrowIfCancellationRequested();
                    var remaining = (int)Math.Min(chunkSize, totalBytes - sent);
                    var read = await fs.ReadAsync(rented.AsMemory(0, remaining), token).ConfigureAwait(false);
                    if (read == 0) yield break;

                    // Allocate a precisely-sized array that SignalR's JSON HubProtocol can base64-encode
                    // without us racing with the serializer over a pooled buffer.
                    var chunk = new byte[read];
                    Buffer.BlockCopy(rented, 0, chunk, 0, read);
                    sent += read;

                    progress?.Report(new G9DtUploadClientProgress(
                        UploadId: uploadId,
                        BytesSent: sent,
                        BytesAcknowledged: readAcked(),
                        TotalBytes: totalBytes,
                        BytesPerSecond: stopwatch.Elapsed.TotalSeconds > 0 ? (sent - startOffset) / stopwatch.Elapsed.TotalSeconds : 0,
                        Elapsed: stopwatch.Elapsed));

                    yield return chunk;
                }
            }
            finally
            {
                pool.Return(rented);
            }
        }

        // Documented streaming pattern: pass the IAsyncEnumerable as a hub argument.
        return await _connection.InvokeAsync<G9DtUploadResult>(
            _options.UploadMethod,
            uploadId, ProduceChunks(ct),
            ct).ConfigureAwait(false);
    }

    private static string ComputeUploadId(FileInfo file)
    {
        // Deterministic id: SHA-256 of "absolutePath|length|lastWriteUtcTicks".
        var key = $"{file.FullName.ToLowerInvariant()}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<string> ComputeSha256HexAsync(FileInfo file, CancellationToken ct)
    {
        await using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
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
