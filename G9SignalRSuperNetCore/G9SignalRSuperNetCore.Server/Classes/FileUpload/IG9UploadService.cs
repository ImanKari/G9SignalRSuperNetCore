namespace G9SignalRSuperNetCore.Server.Classes.FileUpload;

/// <summary>
///     Server-side resumable file-upload service. Tracks per-id partial files so a hub method
///     can begin, resume, and complete an upload safely.
/// </summary>
public interface IG9UploadService
{
    /// <summary>
    ///     Begins or resumes an upload. Returns the byte offset the client should start from.
    /// </summary>
    /// <param name="uploadId">Stable upload identifier (e.g. <c>SHA-256(path|size|mtime)</c>).</param>
    /// <param name="fileName">Final file name on disk (path components are stripped).</param>
    /// <param name="totalBytes">Declared total size of the file.</param>
    /// <param name="chunkSize">Client's preferred chunk size (the server may cap this).</param>
    /// <param name="declaredSha256Hex">Lower-case hex SHA-256 of the source file the client is uploading.</param>
    /// <param name="ct">Cancellation.</param>
    ValueTask<G9DtBeginUploadResult> BeginAsync(
        string uploadId,
        string fileName,
        long totalBytes,
        int chunkSize,
        string declaredSha256Hex,
        CancellationToken ct = default);

    /// <summary>
    ///     Appends a sequence of byte chunks to the partial for <paramref name="uploadId"/>.
    /// </summary>
    /// <param name="uploadId">The upload id returned from <see cref="BeginAsync"/>.</param>
    /// <param name="chunks">Async stream of byte chunks (each non-empty).</param>
    /// <param name="onProgress">Invoked at the configured ack cadence with bytes-received-so-far.</param>
    /// <param name="ct">Cancellation.</param>
    ValueTask<G9DtUploadResult> AppendChunksAsync(
        string uploadId,
        IAsyncEnumerable<byte[]> chunks,
        Func<long, ValueTask>? onProgress,
        CancellationToken ct = default);

    /// <summary>
    ///     Removes partials older than <see cref="G9DtUploadOptions.PartialTtl"/>.
    /// </summary>
    /// <returns>The number of partials deleted.</returns>
    int CleanupExpiredPartials();
}
