using Microsoft.AspNetCore.SignalR;

namespace G9SignalRSuperNetCore.WebServer;

/// <summary>
///     SignalR hub exposing the server-side log feed to a connected browser. The
///     <c>/logs</c> page calls <see cref="Subscribe(CancellationToken)"/> through SignalR's
///     server-to-client streaming API to receive entries as they are produced.
/// </summary>
/// <remarks>
///     The hub is mounted at <c>/__g9logs</c>. The route is intentionally not in the typed
///     <c>IChatClient</c> contract because the log feed is server-internal infrastructure, not
///     part of the sample chat domain.
/// </remarks>
public sealed class G9CServerLogHub : Hub
{
    /// <summary>Public route the hub is mapped to.</summary>
    public const string Route = "/__g9logs";

    private readonly G9CServerLogFeed _feed;

    /// <summary>Initializes the hub.</summary>
    public G9CServerLogHub(G9CServerLogFeed feed) => _feed = feed;

    /// <summary>Streams log entries to the calling client until they unsubscribe.</summary>
    public IAsyncEnumerable<G9DtServerLogEntry> Subscribe(CancellationToken cancellationToken)
        => _feed.Subscribe(cancellationToken);
}
