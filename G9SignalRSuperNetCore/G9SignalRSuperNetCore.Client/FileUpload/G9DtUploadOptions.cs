namespace G9SignalRSuperNetCore.Client.FileUpload;

/// <summary>
///     Reason a single chunk-streaming attempt ended without completing the upload.
///     Surfaced through <see cref="G9DtUploadClientOptions.OnRetry"/> so the host UI can
///     display retries instead of seeing a silent stall during exponential backoff.
/// </summary>
public enum G9EUploadRetryReason
{
    /// <summary>The previous attempt threw an exception (transport, IO, server fault).</summary>
    AttemptFailed,

    /// <summary>The server reported the chunk stream ended early — partial preserved for resume.</summary>
    Interrupted
}

/// <summary>Diagnostic info passed to <see cref="G9DtUploadClientOptions.OnRetry"/>.</summary>
/// <param name="Reason">Why a retry is being scheduled.</param>
/// <param name="Attempt">Zero-based attempt index that just finished.</param>
/// <param name="MaxRetries">The configured retry budget.</param>
/// <param name="BytesAlreadyOnServer">Server-side byte offset that the next attempt will resume from.</param>
/// <param name="Backoff">Delay before the next attempt starts.</param>
/// <param name="Exception">Underlying exception, if any (null for <see cref="G9EUploadRetryReason.Interrupted"/>).</param>
public readonly record struct G9DtUploadRetryInfo(
    G9EUploadRetryReason Reason,
    int Attempt,
    int MaxRetries,
    long BytesAlreadyOnServer,
    TimeSpan Backoff,
    Exception? Exception);

/// <summary>Knobs for the client-side resumable file uploader.</summary>
public sealed class G9DtUploadClientOptions
{
    /// <summary>
    ///     Bytes per chunk. Default 64 KB. The SignalR server caps a single hub message at
    ///     <c>HubOptions.MaximumReceiveMessageSize</c>
    ///     (the G9 server raises this to 4 MB by default).
    /// </summary>
    public int ChunkSize { get; set; } = 64 * 1024;

    /// <summary>How long to wait for a reconnect before giving up. Default 2 minutes.</summary>
    public TimeSpan ReconnectGrace { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Maximum retries (with exponential backoff) before failing the upload. Default 5.</summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>Server hub method name for <c>BeginUploadAsync</c>. Defaults to <c>"BeginUpload"</c>.</summary>
    public string BeginMethod { get; set; } = "BeginUpload";

    /// <summary>Server hub method name for chunk streaming. Defaults to <c>"UploadChunks"</c>.</summary>
    public string UploadMethod { get; set; } = "UploadChunks";

    /// <summary>
    ///     Optional diagnostic callback invoked on every retry. Useful for surfacing transport
    ///     errors or interruptions in a host UI without parsing exception messages.
    /// </summary>
    public Action<G9DtUploadRetryInfo>? OnRetry { get; set; }
}
