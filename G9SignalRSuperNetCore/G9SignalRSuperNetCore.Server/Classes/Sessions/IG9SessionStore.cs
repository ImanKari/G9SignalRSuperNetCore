using G9SignalRSuperNetCore.Server.Classes.Abstracts;

namespace G9SignalRSuperNetCore.Server.Classes.Sessions;

/// <summary>
///     Abstraction over the per-user session storage used by hubs that derive from
///     <see cref="G9AHubBaseWithSession{TTargetClass,TClientSideMethodsInterface,TSession}"/>
///     or <see cref="G9AHubBaseWithSessionAndJWTAuth{TTargetClass,TClientSideMethodsInterface,TSession}"/>.
/// </summary>
/// <typeparam name="TSession">The user session type, derived from <see cref="G9ASession"/>.</typeparam>
/// <remarks>
///     <para>
///         Implementations MUST be thread-safe. The default in-memory implementation
///         (<see cref="G9CInMemorySessionStore{TSession}"/>) uses lock-free atomic operations.
///     </para>
///     <para>
///         For horizontal scale-out across multiple server processes, replace the default
///         registration with a distributed implementation (e.g. Redis-backed) and ensure
///         <see cref="GetOrCreateAsync"/> and <see cref="ReleaseAsync"/> coordinate across
///         all processes.
///     </para>
/// </remarks>
public interface IG9SessionStore<TSession> where TSession : G9ASession, new()
{
    /// <summary>
    ///     Atomically increments the connection counter for the given session identifier.
    ///     Creates the session if it does not already exist using <paramref name="factory"/>.
    /// </summary>
    /// <param name="sessionId">The unique session identifier (typically the user identifier or connection id).</param>
    /// <param name="factory">Factory invoked exactly once if a new session must be created.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session associated with <paramref name="sessionId"/> after the increment.</returns>
    ValueTask<TSession> GetOrCreateAsync(string sessionId, Func<TSession> factory, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Atomically decrements the connection counter for the given session identifier.
    ///     Removes the session if the counter transitions to zero.
    /// </summary>
    /// <param name="sessionId">The unique session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The remaining connection count after the decrement, or -1 if the session was not present.</returns>
    ValueTask<int> ReleaseAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Tries to retrieve an existing session without changing its connection counter.
    /// </summary>
    /// <param name="sessionId">The unique session identifier.</param>
    /// <param name="session">When this method returns, contains the session if found; otherwise, null.</param>
    /// <returns>true if the session is present; otherwise false.</returns>
    bool TryGet(string sessionId, out TSession? session);

    /// <summary>
    ///     Returns true when a session with the given identifier has at least one active connection.
    /// </summary>
    bool IsConnected(string sessionId);

    /// <summary>
    ///     Removes any sessions whose <see cref="G9ASession.LastActivityDateTime"/> is older than
    ///     <see cref="DateTime.UtcNow"/> minus <paramref name="threshold"/>.
    /// </summary>
    /// <returns>The number of sessions removed.</returns>
    int CleanupExpiredSessions(TimeSpan threshold);
}
