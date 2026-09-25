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
    /// <param name="uploadId">The upload id returned from <see cref="BeginAsync(string, string, long, int, string, CancellationToken)"/>.</param>
    /// <param name="chunks">Async stream of byte chunks (each non-empty).</param>
    /// <param name="onProgress">Invoked at the configured ack cadence with bytes-received-so-far.</param>
    /// <param name="ct">Cancellation.</param>
    ValueTask<G9DtUploadResult> AppendChunksAsync(
        string uploadId,
        IAsyncEnumerable<byte[]> chunks,
        Func<long, ValueTask>? onProgress,
        CancellationToken ct = default);

    /// <summary>
    ///     Removes partials older than <see cref="G9DtUploadOptions.PartialTtl"/>. Called periodically by
    ///     <see cref="G9CUploadCleanupService"/> (2.9) every <see cref="G9DtUploadOptions.CleanupInterval"/>.
    /// </summary>
    /// <returns>The number of files deleted (a partial's <c>.bin</c> and its <c>.meta</c> sidecar count separately).</returns>
    int CleanupExpiredPartials();

    /// <summary>
    ///     Begins or resumes a server-to-client download of <paramref name="serverRelativePath"/>.
    ///     Returns the file's total length and SHA-256 so the client can verify after the last
    ///     chunk arrives. The path is resolved against
    ///     <see cref="G9DtUploadOptions.RootDirectory"/> and <c>..</c> traversal is rejected.
    /// </summary>
    /// <param name="serverRelativePath">Path relative to the upload root.</param>
    /// <param name="resumeFrom">Byte offset the client already has on disk.</param>
    /// <param name="chunkSize">Client-preferred chunk size; the server may cap this.</param>
    /// <param name="ct">Cancellation.</param>
    ValueTask<G9DtBeginDownloadResult> BeginDownloadAsync(
        string serverRelativePath,
        long resumeFrom,
        int chunkSize,
        CancellationToken ct = default);

    /// <summary>
    ///     Streams chunks from <paramref name="serverRelativePath"/> starting at
    ///     <paramref name="resumeFrom"/>. Each yielded array is sized to <paramref name="chunkSize"/>
    ///     except possibly the last.
    /// </summary>
    IAsyncEnumerable<byte[]> StreamFileAsync(
        string serverRelativePath,
        long resumeFrom,
        int chunkSize,
        CancellationToken ct = default);

    // ---- 2.9: owner-aware overloads ------------------------------------------------------------------------------
    // Default implementations keep existing custom implementations of this interface compiling. They ignore the owner
    // (the behaviour of G9CUploadService with PerUserNamespace off); G9CUploadService overrides all four.

    /// <summary>
    ///     Begins or resumes an upload on behalf of <paramref name="ownerId"/> (2.9). With
    ///     <see cref="G9DtUploadOptions.PerUserNamespace"/> on, the upload is kept apart from every other owner's, and the
    ///     owner is passed to <see cref="G9DtUploadOptions.Authorize"/>.
    /// </summary>
    /// <param name="ownerId">Who is uploading, typically <c>Context.UserIdentifier</c>; <c>null</c> for anonymous.</param>
    /// <param name="uploadId">Stable upload identifier (e.g. <c>SHA-256(path|size|mtime)</c>).</param>
    /// <param name="fileName">Declared file name (path components are stripped).</param>
    /// <param name="totalBytes">Declared total size of the file.</param>
    /// <param name="chunkSize">Client's preferred chunk size (the server may cap this).</param>
    /// <param name="declaredSha256Hex">Lower-case hex SHA-256 of the source file the client is uploading.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="InvalidOperationException">
    ///     Message <see cref="Errors.G9CErrorCodes.UploadForbidden"/> when the authorization hook refuses.
    /// </exception>
    ValueTask<G9DtBeginUploadResult> BeginAsync(
        string? ownerId,
        string uploadId,
        string fileName,
        long totalBytes,
        int chunkSize,
        string declaredSha256Hex,
        CancellationToken ct = default)
        => BeginAsync(uploadId, fileName, totalBytes, chunkSize, declaredSha256Hex, ct);

    /// <summary>
    ///     Appends chunks to the upload <paramref name="uploadId"/> of <paramref name="ownerId"/> (2.9). Pass the same
    ///     owner as to <see cref="BeginAsync(string?, string, string, long, int, string, CancellationToken)"/>.
    /// </summary>
    /// <param name="ownerId">Who is uploading, typically <c>Context.UserIdentifier</c>; <c>null</c> for anonymous.</param>
    /// <param name="uploadId">The upload id passed to <c>BeginAsync</c>.</param>
    /// <param name="chunks">Async stream of byte chunks (each non-empty).</param>
    /// <param name="onProgress">Invoked at the configured ack cadence with bytes-received-so-far.</param>
    /// <param name="ct">Cancellation.</param>
    ValueTask<G9DtUploadResult> AppendChunksAsync(
        string? ownerId,
        string uploadId,
        IAsyncEnumerable<byte[]> chunks,
        Func<long, ValueTask>? onProgress,
        CancellationToken ct = default)
        => AppendChunksAsync(uploadId, chunks, onProgress, ct);

    /// <summary>
    ///     Begins or resumes a download on behalf of <paramref name="ownerId"/> (2.9); the owner is passed to
    ///     <see cref="G9DtUploadOptions.Authorize"/>.
    /// </summary>
    /// <param name="ownerId">Who is downloading, typically <c>Context.UserIdentifier</c>; <c>null</c> for anonymous.</param>
    /// <param name="serverRelativePath">Path relative to the upload root.</param>
    /// <param name="resumeFrom">Byte offset the client already has on disk.</param>
    /// <param name="chunkSize">Client-preferred chunk size; the server may cap this.</param>
    /// <param name="ct">Cancellation.</param>
    ValueTask<G9DtBeginDownloadResult> BeginDownloadAsync(
        string? ownerId,
        string serverRelativePath,
        long resumeFrom,
        int chunkSize,
        CancellationToken ct = default)
        => BeginDownloadAsync(serverRelativePath, resumeFrom, chunkSize, ct);

    /// <summary>
    ///     Streams a file on behalf of <paramref name="ownerId"/> (2.9); the owner is passed to
    ///     <see cref="G9DtUploadOptions.Authorize"/>.
    /// </summary>
    /// <param name="ownerId">Who is downloading, typically <c>Context.UserIdentifier</c>; <c>null</c> for anonymous.</param>
    /// <param name="serverRelativePath">Path relative to the upload root.</param>
    /// <param name="resumeFrom">Byte offset to start from.</param>
    /// <param name="chunkSize">Chunk size; the server may cap this.</param>
    /// <param name="ct">Cancellation.</param>
    IAsyncEnumerable<byte[]> StreamFileAsync(
        string? ownerId,
        string serverRelativePath,
        long resumeFrom,
        int chunkSize,
        CancellationToken ct = default)
        => StreamFileAsync(serverRelativePath, resumeFrom, chunkSize, ct);
}
