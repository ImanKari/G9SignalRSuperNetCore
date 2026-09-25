namespace G9SignalRSuperNetCore.Server.Classes.FileUpload;

/// <summary>
///     Configuration knobs for the resumable file-upload service.
/// </summary>
public sealed class G9DtUploadOptions
{
    /// <summary>Root folder where committed files are stored. Default: <c>./uploads</c> (created on demand).</summary>
    public string RootDirectory { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "uploads");

    /// <summary>Subfolder under <see cref="RootDirectory"/> for in-flight (partial) uploads. Default: <c>.partial</c>.</summary>
    public string PartialSubdirectory { get; set; } = ".partial";

    /// <summary>Maximum permitted size for a single upload (bytes). Defaults to 5 GB.</summary>
    public long MaxBytes { get; set; } = 5L * 1024 * 1024 * 1024;

    /// <summary>How long an abandoned partial may live before it is purged. Default: 24 hours.</summary>
    public TimeSpan PartialTtl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Push a server-acknowledged progress callback to the client every N chunks. Set to 0 to disable.</summary>
    public int AckEveryNChunks { get; set; } = 16;

    /// <summary>The full path of the partials directory (computed).</summary>
    public string PartialDirectory => Path.Combine(RootDirectory, PartialSubdirectory);

    /// <summary>
    ///     2.9: keep each owner's in-flight uploads apart. When on, and a hub passes a non-empty <c>ownerId</c> to
    ///     <c>BeginAsync</c> / <c>AppendChunksAsync</c>, the partial and its metadata are stored under an id derived from
    ///     the owner AND the upload id, so two users can never resume, append to, or collide with each other's uploads,
    ///     even when their clients pick the same upload id. Uploads without an owner share one anonymous namespace.
    ///     Default: <c>false</c> (the pre-2.9 layout, keyed by upload id only).
    /// </summary>
    public bool PerUserNamespace { get; set; }

    /// <summary>
    ///     2.9: commit each upload under a random name, <c>{32 hex chars}{extension}</c> (the extension is lower-cased,
    ///     limited to <c>[a-z0-9.]</c> and 16 characters), instead of the name the client declared. The client-chosen
    ///     name then never touches the file system, and two uploads can never be confused by name. The committed name
    ///     is returned in <see cref="G9DtUploadResult.StoredFileName"/>; store it next to the declared name in your own
    ///     records. Because names no longer identify content, the "already committed under this name" fast path of
    ///     <c>BeginAsync</c> is skipped. Default: <c>false</c>.
    /// </summary>
    public bool RandomizeCommittedNames { get; set; }

    /// <summary>
    ///     2.9: how often the hosted <see cref="G9CUploadCleanupService"/> calls
    ///     <see cref="IG9UploadService.CleanupExpiredPartials"/> to purge partials older than <see cref="PartialTtl"/>.
    ///     Default: 10 minutes. <c>null</c> (or a non-positive value) turns the periodic cleanup off.
    /// </summary>
    public TimeSpan? CleanupInterval { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    ///     2.9: optional authorization hook, called before every file-transfer operation of <see cref="G9CUploadService"/>
    ///     (<see cref="G9EUploadOperation"/>). Return <c>false</c> to refuse: <c>BeginAsync</c>, <c>BeginDownloadAsync</c>
    ///     and <c>StreamFileAsync</c> then throw <see cref="InvalidOperationException"/> with the message
    ///     <see cref="Errors.G9CErrorCodes.UploadForbidden"/>, and <c>AppendChunksAsync</c> returns a
    ///     <see cref="G9EUploadStatus.Failed"/> result with that error code. <c>null</c> (default) allows everything.
    /// </summary>
    public Func<G9DtUploadAuthorizationContext, CancellationToken, ValueTask<bool>>? Authorize { get; set; }
}
