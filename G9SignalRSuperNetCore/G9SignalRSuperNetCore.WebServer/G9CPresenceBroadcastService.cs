using G9SignalRSuperNetCore.Sample.Shared;
using G9SignalRSuperNetCore.Server.Classes.Presence;
using Microsoft.AspNetCore.SignalR;

namespace G9SignalRSuperNetCore.WebServer;

/// <summary>
///     Background service that drains <see cref="G9CPresenceTracker.Events"/> and broadcasts
///     each transition to every connected ChatHub client through
///     <see cref="IChatClient.PresenceChanged"/>.
/// </summary>
/// <remarks>
///     Lives only on the sample server, not in the library, because the choice of fan-out
///     destination (everyone, by group, by user) is application-specific.
/// </remarks>
public sealed class G9CPresenceBroadcastService : BackgroundService
{
    private readonly G9CPresenceTracker _tracker;
    private readonly IHubContext<Sample.Shared.ChatHub, IChatClient> _hub;
    private readonly ILogger<G9CPresenceBroadcastService> _log;

    /// <summary>Initializes the broadcast service.</summary>
    public G9CPresenceBroadcastService(
        G9CPresenceTracker tracker,
        IHubContext<Sample.Shared.ChatHub, IChatClient> hub,
        ILogger<G9CPresenceBroadcastService> log)
    {
        _tracker = tracker;
        _hub = hub;
        _log = log;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var evt in _tracker.Events.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await _hub.Clients.All.PresenceChanged(evt).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to broadcast presence event for {UserId}", evt.UserId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}
