namespace G9SignalRSuperNetCore.Client.FileUpload;

/// <summary>Mirror of the server-side <c>G9DtBeginDownloadResult</c>.</summary>
public sealed class G9DtBeginDownloadResult
{
    /// <summary>True when the requested file does not exist on the server.</summary>
    public bool NotFound { get; init; }

    /// <summary>Total size of the file in bytes.</summary>
    public long TotalBytes { get; init; }

    /// <summary>Lower-case hex SHA-256 of the file (server-computed once and cached).</summary>
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>Server-suggested chunk size (capped to a safe per-message budget).</summary>
    public int ChunkSize { get; init; }

    /// <summary>The byte offset the client asked to resume from (echoed for sanity).</summary>
    public long ResumeFrom { get; init; }
}

/// <summary>Mirror of the server-side <c>G9DtDownloadResult</c>.</summary>
public sealed class G9DtDownloadResult
{
    /// <summary>Status of the download.</summary>
    public G9EUploadStatus Status { get; init; }

    /// <summary>Total bytes written to the local file (including any pre-existing partial).</summary>
    public long BytesWritten { get; init; }

    /// <summary>Local path where the committed file lives. Null on Interrupted / Failed.</summary>
    public string? LocalPath { get; init; }

    /// <summary>SHA-256 the server reported for the file.</summary>
    public string? Sha256 { get; init; }

    /// <summary>Stable G9 error code on failure.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Human-readable failure description.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>Client-side download progress payload.</summary>
public readonly record struct G9DtDownloadClientProgress(
    string FileName,
    long BytesReceived,
    long TotalBytes,
    double BytesPerSecond,
    System.TimeSpan Elapsed);

/// <summary>Knobs for the client-side resumable file downloader.</summary>
public sealed class G9DtDownloadClientOptions
{
    /// <summary>Bytes per chunk. Default 64 KB.</summary>
    public int ChunkSize { get; set; } = 64 * 1024;

    /// <summary>Maximum retries (with exponential backoff) before failing the download. Default 5.</summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>Server hub method name for the begin handshake. Defaults to <c>"BeginDownload"</c>.</summary>
    public string BeginMethod { get; set; } = "BeginDownload";

    /// <summary>Server hub method name for the chunk stream. Defaults to <c>"DownloadChunks"</c>.</summary>
    public string StreamMethod { get; set; } = "DownloadChunks";

    /// <summary>
    ///     Optional diagnostic callback invoked on every retry. Mirrors
    ///     <see cref="G9DtUploadClientOptions.OnRetry"/>.
    /// </summary>
    public System.Action<G9DtUploadRetryInfo>? OnRetry { get; set; }

    /// <summary>
    ///     When true, an existing local file with the matching SHA-256 short-circuits the
    ///     download (idempotent retry). Default true.
    /// </summary>
    public bool ShortCircuitWhenComplete { get; set; } = true;
}
