namespace G9SignalRSuperNetCore.Server.Classes.FileUpload;

/// <summary>The file-transfer operation a <see cref="G9DtUploadOptions.Authorize"/> hook is asked about (2.9).</summary>
public enum G9EUploadOperation
{
    /// <summary><c>BeginAsync</c>: starting or resuming an upload. <see cref="G9DtUploadAuthorizationContext.TotalBytes"/> is the declared size.</summary>
    Begin,

    /// <summary><c>AppendChunksAsync</c>: sending bytes for an upload. <see cref="G9DtUploadAuthorizationContext.TotalBytes"/> comes from the upload's metadata.</summary>
    Append,

    /// <summary><c>BeginDownloadAsync</c>: starting or resuming a download. <see cref="G9DtUploadAuthorizationContext.TotalBytes"/> is the file's size.</summary>
    BeginDownload,

    /// <summary><c>StreamFileAsync</c>: streaming a file's bytes. <see cref="G9DtUploadAuthorizationContext.TotalBytes"/> is the file's size.</summary>
    Download
}

/// <summary>
///     What a <see cref="G9DtUploadOptions.Authorize"/> hook is asked to allow (2.9): who (<paramref name="OwnerId"/>)
///     wants to do what (<paramref name="Operation"/>) with which file.
/// </summary>
/// <param name="Operation">The operation being attempted.</param>
/// <param name="OwnerId">
///     The owner id the hub passed to the service (typically <c>Context.UserIdentifier</c>), or <c>null</c> when the hub
///     called an overload without one.
/// </param>
/// <param name="UploadId">The upload id for <see cref="G9EUploadOperation.Begin"/> / <see cref="G9EUploadOperation.Append"/>; empty for downloads.</param>
/// <param name="FileName">
///     The sanitized file name: the declared name of an upload (path components stripped), or the requested file of a
///     download (relative to <see cref="G9DtUploadOptions.RootDirectory"/>).
/// </param>
/// <param name="TotalBytes">The declared size of an upload, or the size of the file being downloaded.</param>
public sealed record G9DtUploadAuthorizationContext(
    G9EUploadOperation Operation,
    string? OwnerId,
    string UploadId,
    string FileName,
    long TotalBytes);
