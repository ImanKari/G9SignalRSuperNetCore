using System.Net;

namespace G9SignalRSuperNetCore.Server.Classes.Abstracts;

/// <summary>
///     Represents a base class for managing user session information in a SignalR hub.
/// </summary>
public abstract class G9ASession
{
    /// <summary>
    ///     Gets or sets the number of active connections for the session.
    /// </summary>
    /// <remarks>
    ///     This value is internally managed and is used to keep track of a user's connection count.
    /// </remarks>
    public int ConnectionCounts { internal set; get; }

    /// <summary>
    ///     Gets the IP address of the client associated with this session.
    /// </summary>
    /// <remarks>
    ///     This property is internally set when the session is established.
    /// </remarks>
    public IPAddress? ClientIpAddress { internal set; get; }

    /// <summary>
    ///     Gets or sets the first connection date time for this session.
    /// </summary>
    public DateTime FirstConnectionDateTime { get; internal init; }

    /// <summary>
    ///     Gets or sets the last activity date time for this session.
    /// </summary>
    public DateTime LastActivityDateTime { get; internal set; }

    /// <summary>
    ///     Called when a client disconnects from the hub.
    /// </summary>
    /// <param name="exception">The exception that caused the disconnect, if any.</param>
    /// <returns>A <see cref="Task" /> representing the asynchronous operation.</returns>
    /// <remarks>
    ///     This method can be overridden to perform custom logic when the client disconnects.
    /// </remarks>
    protected virtual Task OnDisconnected(Exception? exception)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Called when a client connects to the hub.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the asynchronous operation.</returns>
    /// <remarks>
    ///     This method can be overridden to perform custom logic when the client connects.
    /// </remarks>
    protected virtual Task OnConnectedAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Called when the session is disposed.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the asynchronous operation.</returns>
    /// <remarks>
    ///     This method can be overridden to perform cleanup logic before the session is disposed.
    /// </remarks>
    protected virtual Task OnDisposeAsync()
    {
        return Task.CompletedTask;
    }
}