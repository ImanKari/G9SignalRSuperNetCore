using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using G9SignalRSuperNetCore.Server.Classes.Attributes;
using G9SignalRSuperNetCore.Server.Classes.Sessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace G9SignalRSuperNetCore.Server.Classes.Abstracts;

/// <summary>
///     A high-performance base class for SignalR hubs that combines JWT authentication
///     with integrated, pluggable per-user session management.
/// </summary>
/// <typeparam name="TTargetClass">The derived hub type.</typeparam>
/// <typeparam name="TSession">The session type derived from <see cref="G9ASession"/>.</typeparam>
/// <typeparam name="TClientSideMethodsInterface">The client-side methods interface.</typeparam>
/// <remarks>
///     <para>
///         The hub is decorated with <see cref="AuthorizeAttribute"/>; clients must present a
///         valid JWT issued by the matching <see cref="G9AHubBaseWithJWTAuth{TTargetClass,TClientSideMethodsInterface}"/> route.
///     </para>
///     <para>
///         The session store is resolved from the dependency injection container; see
///         <see cref="IG9SessionStore{TSession}"/>.
///     </para>
/// </remarks>
[Authorize]
public abstract class G9AHubBaseWithSessionAndJWTAuth<TTargetClass, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TClientSideMethodsInterface, TSession>
    : G9AHubBaseWithJWTAuth<TTargetClass, TClientSideMethodsInterface>
    where TTargetClass : G9AHubBaseWithJWTAuth<TTargetClass, TClientSideMethodsInterface>
    where TClientSideMethodsInterface : class
    where TSession : G9ASession, new()
{
    /// <summary>
    ///     Initializes a new <see cref="G9AHubBaseWithSessionAndJWTAuth{TTargetClass,TClientSideMethodsInterface,TSession}"/>.
    /// </summary>
    [RequiresDynamicCode("SignalR Hub<T> proxies require dynamic code at runtime.")]
    protected G9AHubBaseWithSessionAndJWTAuth() { }

    #region Fields

    private string? _cachedSessionIdentifier;
    private TSession? _cachedSession;
    private IG9SessionStore<TSession>? _sessionStore;

    #endregion

    #region Properties

    private IG9SessionStore<TSession> SessionStore =>
        _sessionStore ??= Context.GetHttpContext()?.RequestServices?.GetRequiredService<IG9SessionStore<TSession>>()
                          ?? throw new InvalidOperationException(
                              $"No IG9SessionStore<{typeof(TSession).Name}> registered. " +
                              $"Call services.AddG9SignalRSuperNetCoreSessionStore<{typeof(TSession).Name}>() during startup.");

    private string SessionUniqueIdentifier
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _cachedSessionIdentifier ??= Context.UserIdentifier ?? Context.ConnectionId;
    }

    /// <summary>
    ///     Gets the session associated with the current authenticated connection.
    /// </summary>
    protected TSession Session
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_cachedSession != null) return _cachedSession;
            if (SessionStore.TryGet(SessionUniqueIdentifier, out var existing) && existing != null)
            {
                _cachedSession = existing;
                return _cachedSession;
            }

            var newSession = new TSession();
            newSession.InitializeActivity();
            _cachedSession = newSession;
            return newSession;
        }
    }

    #endregion

    #region Lifecycle

    /// <inheritdoc />
    public sealed override async Task OnConnectedAsync()
    {
        var identifier = SessionUniqueIdentifier;
        if (!string.IsNullOrEmpty(identifier))
        {
            var httpContext = Context.GetHttpContext();
            _cachedSession = await SessionStore.GetOrCreateAsync(
                identifier,
                () => CreateNewSession(httpContext),
                Context.ConnectionAborted).ConfigureAwait(false);
        }

        await OnConnectedAsyncNext().ConfigureAwait(false);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public sealed override async Task OnDisconnectedAsync(Exception? exception)
    {
        var identifier = SessionUniqueIdentifier;
        if (!string.IsNullOrEmpty(identifier))
        {
            await SessionStore.ReleaseAsync(identifier, CancellationToken.None).ConfigureAwait(false);
            _cachedSession = null;
        }

        await OnDisconnectedAsyncNext(exception).ConfigureAwait(false);
        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    /// <summary>
    ///     Override to add custom logic during the connection lifecycle. Runs before the base implementation.
    /// </summary>
    [G9AttrDenyAccess]
    [G9AttrExcludeFromClientGeneration]
    public virtual Task OnConnectedAsyncNext() => Task.CompletedTask;

    /// <summary>
    ///     Override to add custom logic during the disconnection lifecycle. Runs before the base implementation.
    /// </summary>
    [G9AttrDenyAccess]
    [G9AttrExcludeFromClientGeneration]
    public virtual Task OnDisconnectedAsyncNext(Exception? exception) => Task.CompletedTask;

    #endregion

    #region Helpers

    /// <summary>
    ///     Returns true when at least one connection associated with <paramref name="userId"/> is currently active.
    /// </summary>
    [G9AttrDenyAccess]
    [G9AttrExcludeFromClientGeneration]
    public bool IsUserConnected(string userId) => SessionStore.IsConnected(userId);

    /// <summary>
    ///     Removes sessions whose last-activity timestamp is older than <paramref name="threshold"/>.
    /// </summary>
    /// <returns>The number of sessions removed.</returns>
    [G9AttrDenyAccess]
    [G9AttrExcludeFromClientGeneration]
    public int CleanupExpiredSessions(TimeSpan threshold) => SessionStore.CleanupExpiredSessions(threshold);

    private static TSession CreateNewSession(HttpContext? httpContext)
    {
        var nowUtc = DateTime.UtcNow;
        var session = new TSession
        {
            ClientIpAddress = httpContext?.Connection.RemoteIpAddress,
            FirstConnectionDateTime = nowUtc
        };
        session.InitializeActivity();
        return session;
    }

    #endregion
}
