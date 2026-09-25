namespace G9SignalRSuperNetCore.Server.Classes.Connections;

/// <summary>
///     Process-local index of the live hub connections of each authenticated user (2.9), with the means to end them.
///     Registered by <c>services.AddG9SignalRSuperNetCoreConnectionIndex()</c> and kept current by
///     <see cref="Filters.G9CHubFilter"/> on every connect and disconnect of every hub that runs the filter.
/// </summary>
/// <remarks>
///     <para>A connection is indexed under <c>HubCallerContext.UserIdentifier</c>. Connections without a user identifier
///     are not indexed under any user; only <see cref="AbortConnection"/> reaches them.</para>
///     <para>A connection is added BEFORE the hub's own <c>OnConnectedAsync</c> runs and removed BEFORE the hub's own
///     <c>OnDisconnectedAsync</c> runs, so inside those methods <see cref="Count"/> already includes (or no longer
///     includes) the current connection: <c>Count(user) == 1</c> in <c>OnConnectedAsync</c> means "first connection",
///     and <c>!IsOnline(user)</c> in <c>OnDisconnectedAsync</c> means "last connection gone".</para>
///     <para>The index is per process. In a scaled-out deployment each node sees only its own connections.</para>
/// </remarks>
public interface IG9UserConnectionIndex
{
    /// <summary>A snapshot of the connection ids the user currently has open (empty when none).</summary>
    IReadOnlyCollection<string> GetConnections(string userId);

    /// <summary>How many connections the user currently has open.</summary>
    int Count(string userId);

    /// <summary>Whether the user has at least one open connection.</summary>
    bool IsOnline(string userId);

    /// <summary>A snapshot of the users that have at least one open connection.</summary>
    IReadOnlyCollection<string> OnlineUsers();

    /// <summary>
    ///     Aborts every open connection of the user (for example after a password change or a ban) and returns how many
    ///     were aborted. The connections leave the index when their disconnect has been processed, which happens
    ///     asynchronously shortly after this call.
    /// </summary>
    int AbortUser(string userId);

    /// <summary>Aborts one connection by id; returns <c>false</c> when no such connection is open.</summary>
    bool AbortConnection(string connectionId);
}
