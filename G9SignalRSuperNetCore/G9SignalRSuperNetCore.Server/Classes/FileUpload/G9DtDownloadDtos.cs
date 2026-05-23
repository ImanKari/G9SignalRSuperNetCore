namespace G9SignalRSuperNetCore.Server.Classes.FileUpload;

/// <summary>Result of a <c>BeginDownloadAsync</c> handshake.</summary>
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

/// <summary>Final outcome of a download operation.</summary>
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
