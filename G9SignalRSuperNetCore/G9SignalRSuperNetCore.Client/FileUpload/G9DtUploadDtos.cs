namespace G9SignalRSuperNetCore.Client.FileUpload;

/// <summary>Mirror of the server-side <c>G9DtBeginUploadResult</c>.</summary>
public sealed class G9DtBeginUploadResult
{
    /// <summary>The stable upload identifier echoed back to the client.</summary>
    public string UploadId { get; init; } = string.Empty;

    /// <summary>Bytes already on disk for this upload (zero on first start, &gt;0 on resume).</summary>
    public long BytesAlreadyReceived { get; init; }

    /// <summary>The chunk size the server expects (mirrors the client's request unless capped).</summary>
    public int ChunkSize { get; init; }

    /// <summary>True when the upload has already been committed by a prior call (idempotent retry).</summary>
    public bool AlreadyCompleted { get; init; }

    /// <summary>The server-side path of the already-committed file when <see cref="AlreadyCompleted"/> is true.</summary>
    public string? FinalPath { get; init; }
}

/// <summary>Mirror of the server-side <c>G9DtUploadResult</c>.</summary>
public sealed class G9DtUploadResult
{
    /// <summary>Status of the upload.</summary>
    public G9EUploadStatus Status { get; init; }

    /// <summary>Total bytes written.</summary>
    public long BytesWritten { get; init; }

    /// <summary>Lower-case hex SHA-256 of the committed file when <see cref="Status"/> is <see cref="G9EUploadStatus.Completed"/>.</summary>
    public string? Sha256 { get; init; }

    /// <summary>Final on-disk path of the committed file (when <see cref="Status"/> is <see cref="G9EUploadStatus.Completed"/>).</summary>
    public string? FinalPath { get; init; }

    /// <summary>Stable G9 error code when <see cref="Status"/> is <see cref="G9EUploadStatus.Failed"/>.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Human-readable error description when <see cref="Status"/> is <see cref="G9EUploadStatus.Failed"/>.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>Mirror of the server-side <c>G9EUploadStatus</c>.</summary>
public enum G9EUploadStatus
{
    /// <summary>The upload completed and the file is committed.</summary>
    Completed,

    /// <summary>The chunk stream ended early; the partial is preserved for resume.</summary>
    Interrupted,

    /// <summary>The upload failed and the partial was deleted.</summary>
    Failed
}

/// <summary>Server-pushed progress payload received via the listener interface.</summary>
public sealed class G9DtUploadProgress
{
    /// <summary>The upload's stable identifier.</summary>
    public string UploadId { get; init; } = string.Empty;

    /// <summary>Bytes the server has durably written so far.</summary>
    public long BytesReceived { get; init; }

    /// <summary>Total bytes the client declared at <c>BeginUpload</c>.</summary>
    public long TotalBytes { get; init; }
}

/// <summary>Client-side progress payload combining local and server-acknowledged bytes.</summary>
/// <param name="UploadId">The stable upload identifier.</param>
/// <param name="BytesSent">Bytes the client has handed to the SignalR runtime.</param>
/// <param name="BytesAcknowledged">Bytes the server has confirmed durable.</param>
/// <param name="TotalBytes">Total file size in bytes.</param>
/// <param name="BytesPerSecond">Throughput since the upload began (or resumed).</param>
/// <param name="Elapsed">Wall-clock time since the current attempt started.</param>
public readonly record struct G9DtUploadClientProgress(
    string UploadId,
    long BytesSent,
    long BytesAcknowledged,
    long TotalBytes,
    double BytesPerSecond,
    TimeSpan Elapsed);
