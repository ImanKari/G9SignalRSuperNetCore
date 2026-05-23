namespace G9SignalRSuperNetCore.Server.Classes.Presence;

/// <summary>The reported lifecycle state of a tracked principal.</summary>
public enum G9EPresenceState
{
    /// <summary>The principal is currently online (one or more active connections).</summary>
    Online,
    /// <summary>The principal has gone offline (no active connections).</summary>
    Offline
}

/// <summary>A presence event that the server publishes for online / offline transitions.</summary>
/// <param name="UserId">The user identifier (or connection id when the user is unauthenticated).</param>
/// <param name="State">Whether the principal just came online or went offline.</param>
/// <param name="UtcTimestamp">The wall-clock time of the transition.</param>
/// <param name="ConnectionCount">The user's active connection count after the transition.</param>
public readonly record struct G9DtPresenceEvent(
    string UserId,
    G9EPresenceState State,
    DateTime UtcTimestamp,
    int ConnectionCount);
