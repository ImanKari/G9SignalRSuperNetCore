using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace G9SignalRSuperNetCore.Server.Classes.Connections;

/// <summary>
///     Lock-free, process-local <see cref="IG9UserConnectionIndex"/> (2.9). Holds the <see cref="HubCallerContext"/> of
///     every open connection, so a connection can be aborted from outside the hub (an admin endpoint, a background job).
/// </summary>
/// <remarks>
///     Registered as a singleton by <c>AddG9SignalRSuperNetCoreConnectionIndex()</c> and populated by
///     <see cref="Filters.G9CHubFilter"/>. Every operation is lock-free; the per-user sets are retired with a
///     compare-and-swap before they are removed, so a connect that races the disconnect of the same user's last
///     connection is never lost.
/// </remarks>
public sealed class G9CUserConnectionIndex : IG9UserConnectionIndex
{
    private readonly ConcurrentDictionary<string, Entry> _connections = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, UserConnections> _users = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetConnections(string userId)
    {
        if (string.IsNullOrEmpty(userId) || !_users.TryGetValue(userId, out var set)) return Array.Empty<string>();
        return set.Connections.Keys.ToArray();
    }

    /// <inheritdoc />
    public int Count(string userId) =>
        !string.IsNullOrEmpty(userId) && _users.TryGetValue(userId, out var set) ? set.Connections.Count : 0;

    /// <inheritdoc />
    public bool IsOnline(string userId) =>
        !string.IsNullOrEmpty(userId) && _users.TryGetValue(userId, out var set) && !set.Connections.IsEmpty;

    /// <inheritdoc />
    public IReadOnlyCollection<string> OnlineUsers() =>
        _users.Where(pair => !pair.Value.Connections.IsEmpty).Select(pair => pair.Key).ToArray();

    /// <inheritdoc />
    public int AbortUser(string userId)
    {
        if (string.IsNullOrEmpty(userId) || !_users.TryGetValue(userId, out var set)) return 0;
        var aborted = 0;
        foreach (var context in set.Connections.Values)
        {
            context.Abort();
            aborted++;
        }

        return aborted;
    }

    /// <inheritdoc />
    public bool AbortConnection(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId) || !_connections.TryGetValue(connectionId, out var entry)) return false;
        entry.Context.Abort();
        return true;
    }

    /// <summary>Total open connections in the index, with or without a user.</summary>
    public int ConnectionCount => _connections.Count;

    /// <summary>Adds a connection (called by the hub filter before the hub's <c>OnConnectedAsync</c>).</summary>
    internal void Add(HubCallerContext context)
    {
        var userId = context.UserIdentifier;
        _connections[context.ConnectionId] = new Entry(context, userId);
        if (string.IsNullOrEmpty(userId)) return;

        SpinWait spin = default;
        while (true)
        {
            var set = _users.GetOrAdd(userId, static _ => new UserConnections());
            set.Connections[context.ConnectionId] = context;
            if (!set.IsRetired) return;

            // A concurrent Remove is retiring this set. Step back out of it and retry: the remover either drops the
            // set (and GetOrAdd creates a fresh one) or revives it (and GetOrAdd returns it again).
            set.Connections.TryRemove(context.ConnectionId, out _);
            spin.SpinOnce();
        }
    }

    /// <summary>Removes a connection (called by the hub filter before the hub's <c>OnDisconnectedAsync</c>).</summary>
    internal void Remove(string connectionId)
    {
        if (!_connections.TryRemove(connectionId, out var entry) || string.IsNullOrEmpty(entry.UserId)) return;
        if (!_users.TryGetValue(entry.UserId, out var set)) return;

        set.Connections.TryRemove(connectionId, out _);

        // Retire first, then look again: an Add that slipped in either sees the flag and retries, or is seen here.
        // The loop covers a second remover that found the set empty while this one held the flag.
        while (set.Connections.IsEmpty && set.TryRetire())
        {
            if (set.Connections.IsEmpty)
            {
                _users.TryRemove(new KeyValuePair<string, UserConnections>(entry.UserId, set));
                return;
            }

            set.Revive();
        }
    }

    private sealed record Entry(HubCallerContext Context, string? UserId);

    private sealed class UserConnections
    {
        private int _retired;

        public ConcurrentDictionary<string, HubCallerContext> Connections { get; } = new(StringComparer.Ordinal);

        public bool IsRetired => Volatile.Read(ref _retired) == 1;

        public bool TryRetire() => Interlocked.CompareExchange(ref _retired, 1, 0) == 0;

        public void Revive() => Volatile.Write(ref _retired, 0);
    }
}
