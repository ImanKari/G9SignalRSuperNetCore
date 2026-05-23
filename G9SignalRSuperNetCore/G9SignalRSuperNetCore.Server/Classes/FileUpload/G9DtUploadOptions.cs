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
}
