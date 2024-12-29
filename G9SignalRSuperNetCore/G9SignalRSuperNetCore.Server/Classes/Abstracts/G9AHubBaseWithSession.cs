using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using G9SignalRSuperNetCore.Server.Classes.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;

namespace G9SignalRSuperNetCore.Server.Classes.Abstracts;

/// <summary>
///     A high-performance base class for SignalR hubs with integrated session management.
///     Provides thread-safe operations and optimized memory usage for high-traffic scenarios.
/// </summary>
/// <typeparam name="TTargetClass">
///     The derived hub class inheriting from
///     <see
///         cref="G9AHubBaseWithSessionAndJWTAuth{TTargetClass, TClientSideMethodsInterface, TSession}" />
///     .
/// </typeparam>
/// <typeparam name="TSession">
///     The session class derived from <see cref="G9ASession" /> that stores user session details.
/// </typeparam>
/// <typeparam name="TClientSideMethodsInterface">
///     An interface that defines client-side methods which can be called from the server.
/// </typeparam>
public abstract class G9AHubBaseWithSession<TTargetClass, TClientSideMethodsInterface, TSession>
    : G9AHubBase<TTargetClass, TClientSideMethodsInterface>
    where TTargetClass : G9AHubBase<TTargetClass, TClientSideMethodsInterface>
    where TClientSideMethodsInterface : class
    where TSession : G9ASession, new()
{
    #region Static Fields And Properties

    /// <summary>
    ///     Cached GUID key for the hub type to avoid repeated reflection calls.
    /// </summary>
    private static readonly Guid HubTypeKey = typeof(TTargetClass).GUID;

    /// <summary>
    ///     Thread-safe dictionary storing session data segmented by hub type.
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, TSession>>
        HubSessionStore = new();

    /// <summary>
    ///     Cached dictionary instance for the current hub type's sessions.
    /// </summary>
    private static readonly ConcurrentDictionary<string, TSession> CurrentHubSessions =
        HubSessionStore.GetOrAdd(HubTypeKey, _ => new ConcurrentDictionary<string, TSession>());

    #endregion

    #region Instance Fields And Properties

    /// <summary>
    ///     Cached session identifier to avoid repeated string concatenations.
    /// </summary>
    private string? _cachedSessionIdentifier;

    /// <summary>
    ///     Gets the unique identifier for the current connection session.
    /// </summary>
    private string SessionUniqueIdentifier
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _cachedSessionIdentifier ??= Context.UserIdentifier ?? Context.ConnectionId;
    }

    /// <summary>
    ///     Cached session instance for the current connection.
    /// </summary>
    private TSession? _cachedSession;

    /// <summary>
    ///     Gets the session associated with the current connection.
    /// </summary>
    protected TSession Session
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_cachedSession != null) return _cachedSession;
            _cachedSession = CurrentHubSessions.GetOrAdd(SessionUniqueIdentifier, _ => new TSession());
            return _cachedSession;
        }
    }

    #endregion

    #region Methods

    /// <summary>
    ///     Handles client connection to the hub with optimized session management.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override async Task OnConnectedAsync()
    {
        var identifier = SessionUniqueIdentifier;
        if (string.IsNullOrEmpty(identifier)) return;

        var httpContext = Context.GetHttpContext();

        var session = CurrentHubSessions.AddOrUpdate(
            identifier,
            _ => CreateNewSession(httpContext),
            (_, existingSession) =>
            {
                existingSession.ConnectionCounts++;
                existingSession.LastActivityDateTime = DateTime.Now;
                return existingSession;
            });

        _cachedSession = session;

        await OnConnectedAsyncNext().ConfigureAwait(false);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Creates a new session instance with initial values.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TSession CreateNewSession(HttpContext? httpContext)
    {
        var newSession = new TSession
        {
            ClientIpAddress = httpContext?.Connection.RemoteIpAddress,
            ConnectionCounts = 1,
            LastActivityDateTime = DateTime.Now,
            FirstConnectionDateTime = DateTime.Now
        };
        return newSession;
    }

    /// <summary>
    ///     Virtual method for additional connection handling in derived classes.
    /// </summary>
    [G9AttrDenyAccess]
    [G9AttrExcludeFromClientGeneration]
    public virtual Task OnConnectedAsyncNext()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Handles client disconnection with optimized cleanup operations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override async Task OnDisconnectedAsync(Exception? exception)
    {
        var identifier = SessionUniqueIdentifier;
        if (string.IsNullOrEmpty(identifier)) return;

        if (CurrentHubSessions.TryGetValue(identifier, out var session))
        {
            session.ConnectionCounts--;
            session.LastActivityDateTime = DateTime.Now;

            if (session.ConnectionCounts <= 0)
            {
                CurrentHubSessions.TryRemove(identifier, out _);
                _cachedSession = null;
            }
        }

        await OnDisconnectedAsyncNext(exception).ConfigureAwait(false);
        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    /// <summary>
    ///     Virtual method for additional disconnection handling in derived classes.
    /// </summary>
    [G9AttrDenyAccess]
    [G9AttrExcludeFromClientGeneration]
    public virtual Task OnDisconnectedAsyncNext(Exception? exception)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Checks if a user is currently connected to the hub.
    /// </summary>
    [G9AttrDenyAccess]
    [G9AttrExcludeFromClientGeneration]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsUserConnected(string userId)
    {
        return !string.IsNullOrEmpty(userId) && CurrentHubSessions.ContainsKey(userId);
    }

    /// <summary>
    ///     Removes expired sessions based on configurable threshold.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CleanupExpiredSessions(TimeSpan threshold)
    {
        var cutoffTime = DateTime.Now.Subtract(threshold);
        var expiredUsers = CurrentHubSessions
            .Where(kvp => kvp.Value.LastActivityDateTime < cutoffTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var userId in expiredUsers) CurrentHubSessions.TryRemove(userId, out _);
    }

    #endregion
}