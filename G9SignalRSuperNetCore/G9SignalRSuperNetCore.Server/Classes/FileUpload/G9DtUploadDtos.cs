namespace G9SignalRSuperNetCore.Server.Classes.FileUpload;

/// <summary>
///     Result of <c>BeginUploadAsync</c>. Tells the client how many bytes the server has
///     already received for this <see cref="UploadId"/> so the client can resume from that
///     offset.
/// </summary>
public sealed class G9DtBeginUploadResult
{
    /// <summary>The stable upload identifier echoed back to the client.</summary>
    public required string UploadId { get; init; }

    /// <summary>Bytes already on disk for this upload (zero on first start, &gt;0 on resume).</summary>
    public required long BytesAlreadyReceived { get; init; }

    /// <summary>The chunk size the server expects (mirrors the client's request unless capped).</summary>
    public required int ChunkSize { get; init; }

    /// <summary>True when the upload has already been committed by a prior call (idempotent retry).</summary>
    public bool AlreadyCompleted { get; init; }

    /// <summary>The server-side path of the already-committed file when <see cref="AlreadyCompleted"/> is true.</summary>
    public string? FinalPath { get; init; }
}

/// <summary>
///     Result of <c>UploadChunksAsync</c>.
/// </summary>
public sealed class G9DtUploadResult
{
    /// <summary>Status of the upload.</summary>
    public required G9EUploadStatus Status { get; init; }

    /// <summary>Total bytes written (matches the declared length on success).</summary>
    public required long BytesWritten { get; init; }

    /// <summary>Lower-case hex SHA-256 of the committed file (when <see cref="Status"/> is <see cref="G9EUploadStatus.Completed"/>).</summary>
    public string? Sha256 { get; init; }

    /// <summary>Final on-disk path of the committed file (set when <see cref="Status"/> is <see cref="G9EUploadStatus.Completed"/>).</summary>
    public string? FinalPath { get; init; }

    /// <summary>Stable G9 error code when <see cref="Status"/> is <see cref="G9EUploadStatus.Failed"/>.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Human-readable error description when <see cref="Status"/> is <see cref="G9EUploadStatus.Failed"/>.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>Outcome enum for <see cref="G9DtUploadResult"/>.</summary>
public enum G9EUploadStatus
{
    /// <summary>The upload completed and the file is committed.</summary>
    Completed,

    /// <summary>The chunk stream ended early (client cancelled or disconnected); resume is possible.</summary>
    Interrupted,

    /// <summary>The upload failed and the partial was deleted.</summary>
    Failed
}
