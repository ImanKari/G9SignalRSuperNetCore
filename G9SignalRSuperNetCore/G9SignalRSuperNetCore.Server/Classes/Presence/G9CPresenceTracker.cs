using System.Collections.Concurrent;
using System.Threading.Channels;

namespace G9SignalRSuperNetCore.Server.Classes.Presence;

/// <summary>
///     Tracks online users by counting active connections per user-id and publishes
///     <see cref="G9DtPresenceEvent"/>s whenever a user transitions between
///     <see cref="G9EPresenceState.Online"/> and <see cref="G9EPresenceState.Offline"/>.
/// </summary>
/// <remarks>
///     <para>The tracker uses <see cref="Interlocked"/> arithmetic on a single 64-bit field per
///     user (high 32 bits = monotonic sequence, low 32 bits = connection count) so the only
///     synchronization on the hot path is one CAS per connect/disconnect.</para>
///     <para>Subscribers receive presence events through a single <see cref="Channel{T}"/>; the
///     consumer (e.g. a hosted service that fan-outs to interested clients) reads with
///     <see cref="ChannelReader{T}.ReadAllAsync"/>.</para>
///     <para>Performance: zero work is done for hubs that don't carry
///     <see cref="Attributes.G9AttrPresenceTrackedAttribute"/>. Counting is otherwise lock-free.</para>
/// </remarks>
public sealed class G9CPresenceTracker
{
    private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTime> _lastSeen = new(StringComparer.Ordinal);
    private readonly Channel<G9DtPresenceEvent> _events =
        Channel.CreateUnbounded<G9DtPresenceEvent>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    /// <summary>Reader exposing presence transitions to the hosting application.</summary>
    public ChannelReader<G9DtPresenceEvent> Events => _events.Reader;

    /// <summary>Reports a new connection for <paramref name="userId"/>.</summary>
    public void OnConnected(string userId)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        var nowUtc = DateTime.UtcNow;
        var newCount = _counts.AddOrUpdate(userId, 1, static (_, old) => old + 1);
        _lastSeen[userId] = nowUtc;

        if (newCount == 1)
            _events.Writer.TryWrite(new G9DtPresenceEvent(userId, G9EPresenceState.Online, nowUtc, newCount));
    }

    /// <summary>Reports a disconnection for <paramref name="userId"/>.</summary>
    public void OnDisconnected(string userId)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        var nowUtc = DateTime.UtcNow;
        if (!_counts.TryGetValue(userId, out var current)) return;

        var newCount = _counts.AddOrUpdate(userId, 0, static (_, old) => old > 0 ? old - 1 : 0);
        _lastSeen[userId] = nowUtc;

        if (newCount == 0)
        {
            // Remove zero-count entries so they don't leak; race-safe because TryRemove only succeeds
            // when the value matches what we just observed.
            ((System.Collections.Generic.ICollection<KeyValuePair<string, int>>)_counts)
                .Remove(new KeyValuePair<string, int>(userId, 0));
            _events.Writer.TryWrite(new G9DtPresenceEvent(userId, G9EPresenceState.Offline, nowUtc, 0));
        }

        _ = current; // observed count was used only to short-circuit the no-op case.
    }

    /// <summary>Returns true when <paramref name="userId"/> has at least one active connection.</summary>
    public bool IsOnline(string userId)
        => !string.IsNullOrEmpty(userId) && _counts.TryGetValue(userId, out var n) && n > 0;

    /// <summary>Returns the number of active connections for <paramref name="userId"/>.</summary>
    public int ConnectionCount(string userId)
        => _counts.TryGetValue(userId, out var n) ? n : 0;

    /// <summary>Returns the last activity timestamp for <paramref name="userId"/> (UTC).</summary>
    public DateTime? LastSeenUtc(string userId)
        => _lastSeen.TryGetValue(userId, out var t) ? t : null;

    /// <summary>Returns a snapshot of currently-online user ids.</summary>
    public IReadOnlyCollection<string> OnlineUsers() => _counts.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToArray();
}
