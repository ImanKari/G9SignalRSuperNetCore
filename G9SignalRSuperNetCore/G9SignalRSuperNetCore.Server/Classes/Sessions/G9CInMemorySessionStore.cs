using System.Collections.Concurrent;
using G9SignalRSuperNetCore.Server.Classes.Abstracts;

namespace G9SignalRSuperNetCore.Server.Classes.Sessions;

/// <summary>
///     A high-performance, thread-safe, in-process implementation of
///     <see cref="IG9SessionStore{TSession}"/> backed by <see cref="ConcurrentDictionary{TKey, TValue}"/>
///     and lock-free atomic counter mutations on the session itself.
/// </summary>
/// <typeparam name="TSession">The user session type.</typeparam>
/// <remarks>
///     This implementation is correct for a single server process. For horizontal scale-out
///     across multiple processes, replace the registration with a distributed implementation.
/// </remarks>
public sealed class G9CInMemorySessionStore<TSession> : IG9SessionStore<TSession>
    where TSession : G9ASession, new()
{
    private readonly ConcurrentDictionary<string, TSession> _sessions = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<TSession> GetOrCreateAsync(string sessionId, Func<TSession> factory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        ArgumentNullException.ThrowIfNull(factory);

        // GetOrAdd is atomic from the dictionary's perspective; we then atomically bump the counter.
        var session = _sessions.GetOrAdd(sessionId, _ =>
        {
            var s = factory();
            s.InitializeActivity();
            return s;
        });

        session.IncrementConnectionCount();
        session.TouchActivity();
        return ValueTask.FromResult(session);
    }

    /// <inheritdoc />
    public ValueTask<int> ReleaseAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        if (!_sessions.TryGetValue(sessionId, out var session))
            return ValueTask.FromResult(-1);

        session.TouchActivity();
        var remaining = session.DecrementConnectionCount();

        if (remaining <= 0)
        {
            // Best-effort removal; if another connect raced in and re-incremented, leave it in place.
            // We compare-by-reference to avoid removing a different session instance that might have replaced this one.
            if (_sessions.TryGetValue(sessionId, out var current) &&
                ReferenceEquals(current, session) &&
                session.ConnectionCounts <= 0)
            {
                ((ICollection<KeyValuePair<string, TSession>>)_sessions)
                    .Remove(new KeyValuePair<string, TSession>(sessionId, session));
            }
        }

        return ValueTask.FromResult(remaining < 0 ? 0 : remaining);
    }

    /// <inheritdoc />
    public bool TryGet(string sessionId, out TSession? session)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            session = null;
            return false;
        }

        if (_sessions.TryGetValue(sessionId, out var s))
        {
            session = s;
            return true;
        }

        session = null;
        return false;
    }

    /// <inheritdoc />
    public bool IsConnected(string sessionId)
        => !string.IsNullOrEmpty(sessionId)
           && _sessions.TryGetValue(sessionId, out var s)
           && s.ConnectionCounts > 0;

    /// <inheritdoc />
    public int CleanupExpiredSessions(TimeSpan threshold)
    {
        var cutoff = DateTime.UtcNow - threshold;
        var removed = 0;

        foreach (var kvp in _sessions)
        {
            var session = kvp.Value;
            if (session.ConnectionCounts <= 0 && session.LastActivityDateTime < cutoff)
            {
                if (((ICollection<KeyValuePair<string, TSession>>)_sessions).Remove(kvp))
                    removed++;
            }
        }

        return removed;
    }
}
