namespace G9SignalRSuperNetCore.Server.Classes.FileUpload;

/// <summary>
///     Server-pushed progress payload. Sent to the calling client via the listener method
///     <c>UploadProgress(G9DtUploadProgress)</c> at the cadence configured by
///     <see cref="G9DtUploadOptions.AckEveryNChunks"/>.
/// </summary>
public sealed class G9DtUploadProgress
{
    /// <summary>The upload's stable identifier.</summary>
    public required string UploadId { get; init; }

    /// <summary>Bytes the server has durably written so far.</summary>
    public required long BytesReceived { get; init; }

    /// <summary>Total bytes the client declared at <c>BeginUploadAsync</c>.</summary>
    public required long TotalBytes { get; init; }
}
