using G9SignalRSuperNetCore.Server.Classes.Abstracts;

namespace G9SignalRSuperNetCore.Sample.Shared;

/// <summary>
///     Sample plain (no auth) chat hub. The G9 source generator produces a
///     <c>ChatHubClient</c> class for this hub that the console test app uses.
/// </summary>
public class ChatHub : G9AHubBase<ChatHub, IChatClient>
{
    /// <summary>Public route the hub is mapped to.</summary>
    public const string Route = "/chat";

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Sample hub; SignalR Hub<T> requires dynamic code intrinsically.")]
    public ChatHub() { }

    /// <inheritdoc />
    public override string RoutePattern() => Route;

    /// <summary>Broadcasts a chat message to every connected client.</summary>
    /// <param name="user">The display name of the sender.</param>
    /// <param name="message">The message body.</param>
    public Task SendMessage(string user, string message)
        => Clients.All.ReceiveMessage(user, message);

    /// <summary>Returns a curated list of recent messages (mock).</summary>
    /// <returns>A snapshot of the most recent messages.</returns>
    public Task<List<string>> GetRecentMessages()
    {
        var snapshot = new List<string>(8)
        {
            "[2026-05-23 17:00] Iman: Welcome to G9SignalR!",
            "[2026-05-23 17:01] Meti: Hi everyone!",
            "[2026-05-23 17:02] Iman: This is a sample message.",
            "[2026-05-23 17:03] Meti: Generated client is awesome!"
        };
        return Task.FromResult(snapshot);
    }

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        await Clients.Others.UserJoined(Context.ConnectionId).ConfigureAwait(false);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Clients.Others.UserLeft(Context.ConnectionId).ConfigureAwait(false);
        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }
}
