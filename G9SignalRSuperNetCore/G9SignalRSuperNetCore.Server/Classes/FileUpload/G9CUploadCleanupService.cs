using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace G9SignalRSuperNetCore.Server.Classes.FileUpload;

/// <summary>
///     Hosted service that purges abandoned upload partials (2.9): every
///     <see cref="G9DtUploadOptions.CleanupInterval"/> it calls <see cref="IG9UploadService.CleanupExpiredPartials"/>,
///     which removes each partial (its <c>.bin</c> together with its <c>.meta</c>) older than
///     <see cref="G9DtUploadOptions.PartialTtl"/>. Registered by <c>AddG9SignalRSuperNetCoreFileUpload</c>; before 2.9
///     nothing called the cleanup, so abandoned partials stayed on disk for ever.
/// </summary>
/// <remarks>A <c>null</c> or non-positive <see cref="G9DtUploadOptions.CleanupInterval"/> turns it off.</remarks>
public sealed class G9CUploadCleanupService : BackgroundService
{
    private readonly IG9UploadService _uploads;
    private readonly G9DtUploadOptions _options;
    private readonly ILogger<G9CUploadCleanupService> _log;

    /// <summary>Initializes the cleanup service.</summary>
    public G9CUploadCleanupService(
        IG9UploadService uploads,
        IOptions<G9DtUploadOptions> options,
        ILogger<G9CUploadCleanupService> log)
    {
        _uploads = uploads;
        _options = options.Value;
        _log = log;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.CleanupInterval is not { } interval || interval <= TimeSpan.Zero) return;

        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    var removed = _uploads.CleanupExpiredPartials();
                    if (removed > 0) _log.LogInformation("Upload cleanup removed {Count} expired partial file(s)", removed);
                }
                catch (Exception ex)
                {
                    // One failed sweep must not end the service; the next tick tries again.
                    _log.LogWarning(ex, "Upload cleanup failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
    }
}
