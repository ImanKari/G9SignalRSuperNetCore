using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace G9SignalRSuperNetCore.Server.Classes.Abstracts;

public abstract class
    G9AHubBaseWithSession<TTargetClass, TSession, TClientSideMethodsInterface> : G9AHubBase<TTargetClass,
    TClientSideMethodsInterface>
    where TTargetClass : G9AHubBase<TTargetClass, TClientSideMethodsInterface>, new()
    where TClientSideMethodsInterface : class
    where TSession : G9ASession, new()
{
    #region Fields And Properties

    private readonly ConcurrentDictionary<string, G9ASession> _userConnectionCounts = new();

    protected TSession? Session
    {
        get
        {
            var userId = Context.UserIdentifier;

            if (string.IsNullOrEmpty(userId)) return default; // Return null if userId is not set

            // Retrieve the session for the current caller
            if (_userConnectionCounts.TryGetValue(userId, out var session) && session is TSession callerSession)
                return callerSession;

            return default;
        }
    }

    #endregion

    #region Methods

    public sealed override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;

        if (string.IsNullOrEmpty(userId)) return;

        var httpContext = Context.GetHttpContext();
        _userConnectionCounts.AddOrUpdate(
            userId,
            // Factory to create a new G9ASession if the userId doesn't exist
            _ => new TSession
            {
                ClientIpAddress = httpContext?.Connection?.RemoteIpAddress,
                ConnectionCounts = 1
            },
            // Factory to update an existing G9ASession
            (_, session) =>
            {
                session.ConnectionCounts += 1; // Increment connection count
                return session; // Return the updated session
            });

        await OnConnectedAsyncNext();
        await base.OnConnectedAsync();
    }

    public virtual Task OnConnectedAsyncNext()
    {
        return Task.CompletedTask;
    }

    public sealed override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier;

        if (!string.IsNullOrEmpty(userId))
        {
            _userConnectionCounts.AddOrUpdate(
                userId,
                // Factory to handle if the user doesn't exist (unlikely here)
                _ => new TSession
                {
                    ConnectionCounts = 0
                },
                // Update existing session
                (_, session) =>
                {
                    session.ConnectionCounts = Math.Max(0, session.ConnectionCounts - 1);
                    return session;
                });

            // Remove the user entry if ConnectionCounts reaches zero
            if (_userConnectionCounts.TryGetValue(userId, out var session) && session.ConnectionCounts == 0)
                _userConnectionCounts.TryRemove(userId, out _);
        }

        await OnDisconnectedAsyncNext(exception);
        await base.OnDisconnectedAsync(exception);
    }


    public virtual Task OnDisconnectedAsyncNext(Exception? exception)
    {
        return Task.CompletedTask;
    }

    public bool IsUserConnected(string userId)
    {
        return _userConnectionCounts.ContainsKey(userId);
    }

    #endregion
}