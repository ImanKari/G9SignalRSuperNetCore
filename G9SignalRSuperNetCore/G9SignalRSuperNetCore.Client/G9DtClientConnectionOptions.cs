namespace G9SignalRSuperNetCore.Client;

/// <summary>
///     Opt-in connection behaviours of a G9 client. Everything is off by default, and a client that turns nothing on
///     builds and starts its connection exactly as it did before 2.8.0.
/// </summary>
/// <remarks>
///     <para>
///         Set them by overriding <c>ConfigureConnectionOptions</c> on the client (a generated client is a
///         <c>partial</c> class, so the override goes in your own part of it):
///     </para>
///     <code>
/// public partial class ChatHubClient
/// {
///     protected override void ConfigureConnectionOptions(G9DtClientConnectionOptions options)
///     {
///         options.UseStatefulReconnect = true;
///     }
/// }
///     </code>
///     <para>
///         The hook runs while the connection is being built. For a client that builds it in the base constructor
///         (every generated non-JWT client) that is BEFORE the derived constructor body, exactly like
///         <c>RegisterListenerMethods</c>: read constants, statics or field initializers there, not fields the
///         constructor body assigns.
///     </para>
/// </remarks>
public sealed class G9DtClientConnectionOptions
{
    /// <summary>
    ///     Asks for SignalR's stateful reconnect (ASP.NET Core 8+): both sides buffer what they sent, and after a
    ///     short network break the SAME connection resumes (same connection id, no <c>OnDisconnectedAsync</c> /
    ///     <c>OnConnectedAsync</c> on the server, no lost messages) instead of being replaced by a new one.
    ///     Default <c>false</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The server must allow it on the endpoint (<c>AllowStatefulReconnects</c>; the G9 mapping helpers
    ///         take it as a parameter). If it does not, the connection works as an ordinary one.
    ///     </para>
    ///     <para>
    ///         It is agreed during negotiation, so a connection that skipped negotiation does not get it. With
    ///         <see cref="WebSocketsFirst"/> also on, this option wins: the client negotiates, and
    ///         <see cref="WebSocketsFirst"/> is ignored.
    ///     </para>
    /// </remarks>
    public bool UseStatefulReconnect { get; set; }

    /// <summary>
    ///     The client's stateful-reconnect buffer in bytes (what it keeps of its own sent messages until the server
    ///     acknowledges them). <c>null</c> keeps SignalR's default (100,000). Only read when
    ///     <see cref="UseStatefulReconnect"/> is on.
    /// </summary>
    public long? StatefulReconnectBufferSize { get; set; }

    /// <summary>
    ///     Connects over WebSockets directly, without the negotiate request (one round trip less, and no need for
    ///     sticky sessions behind a load balancer), and falls back to ordinary negotiation over every transport
    ///     when that fails. Default <c>false</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The fallback belongs to <c>ConnectAsync</c>: when the WebSocket attempt fails, the same call tries
    ///         once more with negotiation and the transports <c>configureHttpConnection</c> left in place (all of
    ///         them unless you narrowed them), and reports the first failure through <c>StateChanged</c> as
    ///         <see cref="G9EConnectionPhase.TransportFallback"/>. If the second attempt fails too, its exception is
    ///         the one thrown — what a client without this option would have thrown.
    ///     </para>
    ///     <para>
    ///         Not every failure falls back. An authentication failure (401 / 403) does not: the second attempt
    ///         would present the same credentials. Neither does a refusal by the hub itself (a
    ///         <c>HubException</c> from the handshake — the transport worked), a cancellation by the caller, or a
    ///         connection that is disposed or already being started by someone else.
    ///     </para>
    ///     <para>
    ///         Every <c>ConnectAsync</c> starts with WebSockets again, so one outage does not pin the client to
    ///         negotiation. Automatic reconnects are SignalR's own loop and use the settings the connection
    ///         settled on: direct WebSockets when that worked, negotiation after a fallback.
    ///     </para>
    /// </remarks>
    public bool WebSocketsFirst { get; set; }
}
