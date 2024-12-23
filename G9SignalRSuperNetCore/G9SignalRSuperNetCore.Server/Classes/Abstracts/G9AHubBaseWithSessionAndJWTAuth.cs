﻿using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace G9SignalRSuperNetCore.Server.Classes.Abstracts;

/// <summary>
///     A base class for SignalR hubs that includes session management for connected users.
/// </summary>
/// <typeparam name="TTargetClass">
///     The derived hub class inheriting from
///     <see cref="G9AHubBaseWithSessionAndJWTAuth{TTargetClass,TSession,TClientSideMethodsInterface}" />.
/// </typeparam>
/// <typeparam name="TSession">
///     The session class derived from <see cref="G9ASession" /> that stores user session details.
/// </typeparam>
/// <typeparam name="TClientSideMethodsInterface">
///     An interface that defines client-side methods which can be called from the server.
/// </typeparam>
[Authorize]
public abstract class G9AHubBaseWithSessionAndJWTAuth<TTargetClass, TClientSideMethodsInterface, TSession>
    : G9AHubBaseWithJWTAuth<TTargetClass, TClientSideMethodsInterface>
    where TTargetClass : G9AHubBaseWithJWTAuth<TTargetClass, TClientSideMethodsInterface>
    where TClientSideMethodsInterface : class
    where TSession : G9ASession, new()
{
    #region Fields And Properties

    private string _sessionUniqueIdentifier => Context.UserIdentifier ?? Context.ConnectionId;

    /// <summary>
    ///     A thread-safe dictionary that keeps track of user sessions based on their unique user ID.
    /// </summary>
    private readonly ConcurrentDictionary<string, G9ASession> _userConnectionCounts = new();

    /// <summary>
    ///     Gets the session for the current connected user.
    /// </summary>
    /// <remarks>
    ///     Returns null if the user identifier is not set or the session does not exist.
    /// </remarks>
    protected TSession? Session
    {
        get
        {
            if (string.IsNullOrEmpty(_sessionUniqueIdentifier)) return default; 

            // Retrieve the session for the current caller
            if (_userConnectionCounts.TryGetValue(_sessionUniqueIdentifier, out var session) && session is TSession callerSession)
                return callerSession;

            return default;
        }
    }

    #endregion

    #region Methods

    /// <summary>
    ///     Called when a client connects to the hub.
    ///     Manages session creation and connection count updates.
    /// </summary>
    public sealed override async Task OnConnectedAsync()
    {
        

        if (string.IsNullOrEmpty(_sessionUniqueIdentifier)) return;

        var httpContext = Context.GetHttpContext();
        _userConnectionCounts.AddOrUpdate(
            _sessionUniqueIdentifier,
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

    /// <summary>
    ///     Optional method to perform additional tasks after a client connects.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the asynchronous operation.</returns>
    public virtual Task OnConnectedAsyncNext()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Called when a client disconnects from the hub.
    ///     Manages session cleanup and connection count updates.
    /// </summary>
    /// <param name="exception">The exception that caused the disconnect, if any.</param>
    public sealed override async Task OnDisconnectedAsync(Exception? exception)
    {
        
        if (!string.IsNullOrEmpty(_sessionUniqueIdentifier))
        {
            _userConnectionCounts.AddOrUpdate(
                _sessionUniqueIdentifier,
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
            if (_userConnectionCounts.TryGetValue(_sessionUniqueIdentifier, out var session) && session.ConnectionCounts == 0)
                _userConnectionCounts.TryRemove(_sessionUniqueIdentifier, out _);
        }

        await OnDisconnectedAsyncNext(exception);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    ///     Optional method to perform additional tasks after a client disconnects.
    /// </summary>
    /// <param name="exception">The exception that caused the disconnect, if any.</param>
    /// <returns>A <see cref="Task" /> representing the asynchronous operation.</returns>
    public virtual Task OnDisconnectedAsyncNext(Exception? exception)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Determines if a user with the specified user ID is currently connected.
    /// </summary>
    /// <param name="userId">The unique user identifier.</param>
    /// <returns>True if the user is connected; otherwise, false.</returns>
    public bool IsUserConnected(string userId)
    {
        return _userConnectionCounts.ContainsKey(userId);
    }

    #endregion
}