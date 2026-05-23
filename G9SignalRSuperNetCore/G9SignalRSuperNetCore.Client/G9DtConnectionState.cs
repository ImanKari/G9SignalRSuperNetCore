namespace G9SignalRSuperNetCore.Client;

/// <summary>Lifecycle phases reported through <c>G9SignalRSuperNetCoreClient.StateChanged</c>.</summary>
public enum G9EConnectionPhase
{
    /// <summary>The client called <c>ConnectAsync</c> and is establishing the connection.</summary>
    Connecting,
    /// <summary>The connection is open and ready for hub invocations.</summary>
    Connected,
    /// <summary>The initial connect attempt failed (the connection was never established).</summary>
    ConnectFailed,
    /// <summary>The connection dropped and the auto-reconnect policy is retrying.</summary>
    Reconnecting,
    /// <summary>The auto-reconnect policy succeeded and the connection is open again.</summary>
    Reconnected,
    /// <summary>
    ///     The connection is fully disconnected. Triggered both by a graceful
    ///     <c>DisconnectAsync</c> (in which case <see cref="G9DtConnectionState.Detail"/> is null)
    ///     and by terminal reconnect failure (Detail carries the exception summary).
    /// </summary>
    Disconnected
}

/// <summary>One reported lifecycle transition.</summary>
/// <param name="Phase">The phase the client just entered.</param>
/// <param name="UtcTimestamp">When the transition was observed.</param>
/// <param name="Detail">
///     Optional context: the new connection id on <see cref="G9EConnectionPhase.Connected"/> /
///     <see cref="G9EConnectionPhase.Reconnected"/>, an exception summary on
///     <see cref="G9EConnectionPhase.ConnectFailed"/> / <see cref="G9EConnectionPhase.Reconnecting"/>
///     / <see cref="G9EConnectionPhase.Disconnected"/>, or null otherwise.
/// </param>
public readonly record struct G9DtConnectionState(
    G9EConnectionPhase Phase,
    DateTime UtcTimestamp,
    string? Detail);
