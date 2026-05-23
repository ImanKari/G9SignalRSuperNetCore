using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace G9SignalRSuperNetCore.Server.Classes.Groups;

/// <summary>
///     A high-performance, lock-light wrapper around SignalR's group machinery that also maintains
///     an in-process membership index so consumers can answer "who's in group X?" without a
///     round-trip through SignalR. The membership index is process-local; a distributed backplane
///     (Bundle 5) is required for cluster-wide queries.
/// </summary>
/// <typeparam name="THub">The SignalR hub type whose groups are tracked.</typeparam>
/// <remarks>
///     <para>This service is registered as a singleton through
///     <see cref="G9SignalRSuperNetCoreServer.AddG9SignalRSuperNetCoreGroups{THub}"/>.</para>
///     <para>Membership is stored as <see cref="ConcurrentDictionary{TKey,TValue}"/> of
///     groupName → <see cref="ConcurrentDictionary{TKey,TValue}"/> of connectionId → byte. The
///     inner dictionary acts as a concurrent set so add/remove are lock-free at the entry level.</para>
///     <para>Performance: zero-cost when the consumer doesn't call any of the methods. The
///     attribute-driven group joins (<see cref="Attributes.G9AttrAutoJoinGroupAttribute"/>) only
///     allocate when a hub method is decorated.</para>
/// </remarks>
public sealed class G9CGroupManager<THub> where THub : Hub
{
    private readonly IHubContext<THub> _ctx;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _members = new(StringComparer.Ordinal);

    /// <summary>Initializes a new manager for hub <typeparamref name="THub"/>.</summary>
    public G9CGroupManager(IHubContext<THub> ctx) => _ctx = ctx;

    /// <summary>Adds <paramref name="connectionId"/> to <paramref name="groupName"/>.</summary>
    public async Task JoinAsync(string connectionId, string groupName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionId);
        ArgumentException.ThrowIfNullOrEmpty(groupName);

        var set = _members.GetOrAdd(groupName, static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        if (set.TryAdd(connectionId, 0))
        {
            await _ctx.Groups.AddToGroupAsync(connectionId, groupName, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Removes <paramref name="connectionId"/> from <paramref name="groupName"/>.</summary>
    public async Task LeaveAsync(string connectionId, string groupName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionId);
        ArgumentException.ThrowIfNullOrEmpty(groupName);

        if (_members.TryGetValue(groupName, out var set) && set.TryRemove(connectionId, out _))
        {
            await _ctx.Groups.RemoveFromGroupAsync(connectionId, groupName, ct).ConfigureAwait(false);
            // Best-effort prune of empty groups so the index doesn't grow unbounded.
            if (set.IsEmpty) _members.TryRemove(groupName, out _);
        }
    }

    /// <summary>Removes <paramref name="connectionId"/> from every group it's a member of.</summary>
    public async Task LeaveAllAsync(string connectionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionId);

        foreach (var (group, set) in _members)
        {
            if (set.TryRemove(connectionId, out _))
            {
                await _ctx.Groups.RemoveFromGroupAsync(connectionId, group, ct).ConfigureAwait(false);
                if (set.IsEmpty) _members.TryRemove(group, out _);
            }
        }
    }

    /// <summary>Returns a snapshot of the connection ids in <paramref name="groupName"/>.</summary>
    public IReadOnlyCollection<string> GetMembers(string groupName)
    {
        ArgumentException.ThrowIfNullOrEmpty(groupName);
        if (!_members.TryGetValue(groupName, out var set)) return Array.Empty<string>();
        // Snapshot via ToArray to avoid exposing the live concurrent structure.
        return set.Keys.ToArray();
    }

    /// <summary>Returns the count of members in <paramref name="groupName"/>.</summary>
    public int Count(string groupName)
        => _members.TryGetValue(groupName, out var set) ? set.Count : 0;

    /// <summary>Returns a snapshot of every group name currently with at least one member.</summary>
    public IReadOnlyCollection<string> ListGroups() => _members.Keys.ToArray();
}
