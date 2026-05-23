using System.Net;
using System.Threading;

namespace G9SignalRSuperNetCore.Server.Classes.Abstracts;

/// <summary>
///     Represents a base class for managing user session information in a SignalR hub.
/// </summary>
/// <remarks>
///     <para>
///         Instances of this type are shared across all concurrent connections that resolve
///         to the same session identifier. All public state is read-only to consumers and is
///         mutated by the framework through interlocked operations, so the type is safe to
///         use from multiple threads without external synchronization.
///     </para>
///     <para>
///         Custom user-defined fields added in derived classes are NOT automatically thread-safe.
///         Synchronize them yourself if your hub methods write to them concurrently.
///     </para>
/// </remarks>
public abstract class G9ASession
{
    private int _connectionCounts;
    private long _lastActivityTicks;

    /// <summary>
    ///     Gets the number of active connections currently associated with the session.
    ///     Updated atomically by the framework through <see cref="Interlocked"/>.
    /// </summary>
    public int ConnectionCounts => Volatile.Read(ref _connectionCounts);

    /// <summary>
    ///     Gets the IP address of the client that originated the session.
    ///     Set once when the session is created and not mutated thereafter.
    /// </summary>
    public IPAddress? ClientIpAddress { get; internal init; }

    /// <summary>
    ///     Gets the UTC date-time when the session was first established.
    /// </summary>
    public DateTime FirstConnectionDateTime { get; internal init; }

    /// <summary>
    ///     Gets the UTC date-time of the most recent observed activity for the session.
    ///     Read and written atomically through <see cref="Interlocked"/>.
    /// </summary>
    public DateTime LastActivityDateTime => new(Interlocked.Read(ref _lastActivityTicks), DateTimeKind.Utc);

    /// <summary>
    ///     Atomically increments the connection counter and returns the new value.
    ///     Intended for framework use only.
    /// </summary>
    internal int IncrementConnectionCount() => Interlocked.Increment(ref _connectionCounts);

    /// <summary>
    ///     Atomically decrements the connection counter and returns the new value.
    ///     Intended for framework use only.
    /// </summary>
    internal int DecrementConnectionCount() => Interlocked.Decrement(ref _connectionCounts);

    /// <summary>
    ///     Atomically sets the last-activity timestamp to the current UTC time.
    ///     Intended for framework use only.
    /// </summary>
    internal void TouchActivity()
    {
        Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
    }

    /// <summary>
    ///     Initializes the activity timestamps when the session is created.
    ///     Intended for framework use only.
    /// </summary>
    internal void InitializeActivity()
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        Interlocked.Exchange(ref _lastActivityTicks, nowTicks);
    }

    /// <summary>
    ///     Called when a client disconnects from the hub.
    /// </summary>
    /// <param name="exception">The exception that caused the disconnect, if any.</param>
    /// <returns>A <see cref="Task" /> representing the asynchronous operation.</returns>
    /// <remarks>
    ///     Override to perform custom logic when the client disconnects.
    /// </remarks>
    protected virtual Task OnDisconnected(Exception? exception) => Task.CompletedTask;

    /// <summary>
    ///     Called when a client connects to the hub.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the asynchronous operation.</returns>
    /// <remarks>
    ///     Override to perform custom logic when the client connects.
    /// </remarks>
    protected virtual Task OnConnectedAsync() => Task.CompletedTask;

    /// <summary>
    ///     Called when the session is disposed.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the asynchronous operation.</returns>
    /// <remarks>
    ///     Override to perform cleanup logic before the session is disposed.
    /// </remarks>
    protected virtual Task OnDisposeAsync() => Task.CompletedTask;
}
