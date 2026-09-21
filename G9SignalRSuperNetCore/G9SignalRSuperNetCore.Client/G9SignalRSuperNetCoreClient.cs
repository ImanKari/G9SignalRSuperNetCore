using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace G9SignalRSuperNetCore.Client;

/// <summary>
///     A base class for a SignalR client that connects to a server hub.
/// </summary>
/// <typeparam name="TTargetClass">The derived client type (CRTP self-type).</typeparam>
/// <typeparam name="TServerHubMethods">An interface defining methods the client can invoke on the server.</typeparam>
/// <typeparam name="TClientListenerMethods">An interface defining methods the server can invoke on the client.</typeparam>
/// <remarks>
///     <para>
///         This base class is AOT- and trim-safe. It does NOT use runtime proxying
///         (no Castle DynamicProxy, no <see cref="System.Reflection.Emit.AssemblyBuilder"/>).
///         The strongly-typed <c>Server</c> proxy that mirrors <typeparamref name="TServerHubMethods"/>
///         is generated at build time by the
///         <c>G9SignalRSuperNetCore.Server.ClientInterfaceGenerator</c> source generator,
///         or written by hand for full control.
///     </para>
///     <para>
///         Listener registration is also done in generated or hand-written code through
///         <see cref="HubConnectionExtensions.On{T1}(HubConnection, string, Func{T1, Task})"/>.
///         The base class exposes the underlying <see cref="HubConnection"/> via
///         <see cref="Connection"/> for convenience.
///     </para>
/// </remarks>
public abstract class G9SignalRSuperNetCoreClient<TTargetClass, TServerHubMethods, TClientListenerMethods> : IAsyncDisposable
    where TTargetClass : G9SignalRSuperNetCoreClient<TTargetClass, TServerHubMethods, TClientListenerMethods>
    where TServerHubMethods : class
    where TClientListenerMethods : class
{
    /// <summary>
    ///     The underlying SignalR <see cref="HubConnection" />.
    ///     Exposed for advanced scenarios, generated proxies, and listener registration.
    /// </summary>
    public HubConnection Connection { get; protected internal set; } = null!;

    /// <summary>
    ///     The live options of <see cref="Connection"/> while <see cref="G9DtClientConnectionOptions.WebSocketsFirst"/>
    ///     is in effect, otherwise <c>null</c>; and the transports a fallback negotiates with.
    /// </summary>
    private HttpConnectionOptions? _webSocketsFirstOptions;

    private HttpTransportType _negotiatedTransports = HttpTransports.All;

    /// <summary>
    ///     Reports lifecycle changes (connecting, connected, reconnecting, reconnected, disconnected,
    ///     reconnect failed). Subscribe from your UI / logging code to surface accurate connection
    ///     state without poking the underlying <see cref="HubConnection"/> directly.
    /// </summary>
    public event Action<G9DtConnectionState>? StateChanged;

    /// <summary>
    ///     Gets the strongly-typed proxy that invokes server hub methods.
    /// </summary>
    /// <remarks>
    ///     Implementations are produced by the build-time source generator (preferred) or
    ///     written by hand against <see cref="Connection"/>.
    /// </remarks>
    public abstract TServerHubMethods Server { get; }

    /// <summary>
    ///     Initializes a new client connecting to <paramref name="serverUrl"/>.
    /// </summary>
    protected G9SignalRSuperNetCoreClient(
        string serverUrl,
        Func<IHubConnectionBuilder, IHubConnectionBuilder>? customConfigureBuilder = null,
        Action<HttpConnectionOptions>? configureHttpConnection = null)
    {
        if (string.IsNullOrEmpty(serverUrl)) throw new ArgumentException("Value cannot be null or empty.", nameof(serverUrl));
        PrepareConnection(serverUrl, customConfigureBuilder, configureHttpConnection);
    }

    /// <summary>
    ///     Initializes a new client without connecting. Use only from derived classes that need to
    ///     finalize <see cref="Connection"/> later (e.g. JWT-authenticated clients).
    /// </summary>
    protected internal G9SignalRSuperNetCoreClient()
    {
    }

    /// <summary>
    ///     Builds and stores the underlying <see cref="HubConnection"/>. Asks <see cref="ConfigureConnectionOptions"/>
    ///     for the opt-in behaviours first (2.8.0), then registers the listeners through <see cref="RegisterListenerMethods"/>.
    /// </summary>
    protected internal void PrepareConnection(
        string serverUrl,
        Func<IHubConnectionBuilder, IHubConnectionBuilder>? customConfigureBuilder = null,
        Action<HttpConnectionOptions>? configureHttpConnection = null)
    {
        if (string.IsNullOrEmpty(serverUrl)) throw new ArgumentException("Value cannot be null or empty.", nameof(serverUrl));

        // Opt-in behaviours (2.8.0). With nothing turned on, the builder below is the one every earlier version made.
        var connectionOptions = new G9DtClientConnectionOptions();
        ConfigureConnectionOptions(connectionOptions);
        // Stateful reconnect is agreed during negotiation, so it cannot be combined with skipping negotiation: it wins.
        var webSocketsFirst = connectionOptions.WebSocketsFirst && !connectionOptions.UseStatefulReconnect;

        HttpConnectionOptions? httpConnectionOptions = null;
        var negotiatedTransports = HttpTransports.All;

        IHubConnectionBuilder builder = new HubConnectionBuilder()
            .WithUrl(serverUrl, options =>
            {
                configureHttpConnection?.Invoke(options);
                if (!webSocketsFirst) return;

                // SignalR copies this object every time it opens a transport — on StartAsync and on each automatic
                // reconnect — so switching these two properties later changes how the SAME HubConnection connects next.
                // That is what lets the fallback keep the connection, and with it every handler registered on it.
                httpConnectionOptions = options;
                negotiatedTransports = options.Transports;
                ApplyWebSocketsFirst(options);
            })
            .WithAutomaticReconnect(new G9CClientReconnectPolicy());

        if (connectionOptions.UseStatefulReconnect)
        {
            builder = builder.WithStatefulReconnect();
            if (connectionOptions.StatefulReconnectBufferSize is { } bufferSize)
                builder.Services.Configure<HubConnectionOptions>(options => options.StatefulReconnectBufferSize = bufferSize);
        }

        if (customConfigureBuilder != null) builder = customConfigureBuilder(builder);

        Connection = builder.Build();
        Connection.ServerTimeout = TimeSpan.FromSeconds(60);

        // Build() resolves the options, so the callback above has run by now. It has not if customConfigureBuilder
        // swapped the connection factory for one that never reads them; there is nothing to fall back with then.
        _webSocketsFirstOptions = httpConnectionOptions;
        _negotiatedTransports = negotiatedTransports;

        // Lifecycle events — surface them through StateChanged so consumers don't poke
        // Connection.Reconnecting / Reconnected / Closed directly.
        Connection.Reconnecting += err =>
        {
            StateChanged?.Invoke(new G9DtConnectionState(
                G9EConnectionPhase.Reconnecting, DateTime.UtcNow,
                err?.GetType().Name + ": " + err?.Message));
            return Task.CompletedTask;
        };
        Connection.Reconnected += newId =>
        {
            StateChanged?.Invoke(new G9DtConnectionState(
                G9EConnectionPhase.Reconnected, DateTime.UtcNow, newId));
            return Task.CompletedTask;
        };
        Connection.Closed += err =>
        {
            // err is null on a graceful StopAsync; non-null on terminal failure (e.g. reconnect
            // budget exhausted). Either way, the connection is now in the Disconnected state.
            StateChanged?.Invoke(new G9DtConnectionState(
                G9EConnectionPhase.Disconnected, DateTime.UtcNow,
                err is null ? null : err.GetType().Name + ": " + err.Message));
            return Task.CompletedTask;
        };

        RegisterListenerMethods();
    }

    /// <summary>
    ///     When overridden in a derived class (typically a generated partial class), registers
    ///     all listener callbacks on <see cref="Connection"/> using strongly-typed
    ///     <see cref="HubConnectionExtensions.On{T1}(HubConnection, string, Func{T1, Task})"/> overloads.
    ///     The default implementation does nothing.
    /// </summary>
    protected virtual void RegisterListenerMethods()
    {
    }

    /// <summary>
    ///     When overridden in a derived class, turns on the opt-in connection behaviours of
    ///     <see cref="G9DtClientConnectionOptions"/> (stateful reconnect, WebSockets first with a fallback to
    ///     negotiation). The default implementation turns nothing on, and the connection is built as before 2.8.0.
    /// </summary>
    /// <remarks>
    ///     Called by <see cref="PrepareConnection"/> every time it builds a connection, before the
    ///     <c>customConfigureBuilder</c> callback (which therefore still has the last word on the builder). Like
    ///     <see cref="RegisterListenerMethods"/>, it runs inside the base constructor for a client that passes its
    ///     URL to it, so do not read fields the derived constructor body assigns.
    /// </remarks>
    /// <param name="options">A fresh options object with everything off.</param>
    protected virtual void ConfigureConnectionOptions(G9DtClientConnectionOptions options)
    {
    }

    /// <summary>
    ///     Connects to the SignalR server.
    /// </summary>
    public virtual async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        StateChanged?.Invoke(new G9DtConnectionState(G9EConnectionPhase.Connecting, DateTime.UtcNow, null));
        try
        {
            await StartConnectionAsync(cancellationToken).ConfigureAwait(false);
            StateChanged?.Invoke(new G9DtConnectionState(G9EConnectionPhase.Connected, DateTime.UtcNow, Connection.ConnectionId));
        }
        catch (Exception ex)
        {
            StateChanged?.Invoke(new G9DtConnectionState(
                G9EConnectionPhase.ConnectFailed, DateTime.UtcNow,
                ex.GetType().Name + ": " + ex.Message));
            throw;
        }
    }

    /// <summary>
    ///     Starts <see cref="Connection"/>. Without <see cref="G9DtClientConnectionOptions.WebSocketsFirst"/> this is
    ///     <see cref="HubConnection.StartAsync"/> and nothing else. With it, the first attempt goes straight to
    ///     WebSockets without negotiating, and a transport failure is answered with one more attempt that negotiates.
    /// </summary>
    /// <remarks>
    ///     The fallback does NOT rebuild the connection. It switches <see cref="HttpConnectionOptions.SkipNegotiation"/>
    ///     and <see cref="HttpConnectionOptions.Transports"/> on the options object the connection already reads each time
    ///     it opens a transport, so <see cref="Connection"/> stays the same instance: handlers registered on it with
    ///     <c>On&lt;T&gt;()</c>, its timeouts, and references other code holds (a <c>G9CFileUploader</c>) all survive.
    ///     Derived classes that start the connection themselves should call this instead of
    ///     <see cref="HubConnection.StartAsync"/>.
    /// </remarks>
    protected internal async Task StartConnectionAsync(CancellationToken cancellationToken = default)
    {
        var options = _webSocketsFirstOptions;
        if (options is null)
        {
            await Connection.StartAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        // Every start begins with WebSockets again: a fallback caused by an outage, or by the network the device was
        // on at the time, must not pin this client to negotiation. Only while nothing is running — the options of a
        // live connection decide how its automatic reconnects connect, and those should repeat what worked.
        if (Connection.State == HubConnectionState.Disconnected) ApplyWebSocketsFirst(options);

        try
        {
            await Connection.StartAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        catch (Exception ex) when (ShouldFallBackToNegotiation(ex, cancellationToken))
        {
            options.SkipNegotiation = false;
            options.Transports = _negotiatedTransports;
            StateChanged?.Invoke(new G9DtConnectionState(
                G9EConnectionPhase.TransportFallback, DateTime.UtcNow,
                ex.GetType().Name + ": " + ex.Message));
        }

        // Once. If this fails too, its exception is what a client without the option would have thrown.
        await Connection.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ApplyWebSocketsFirst(HttpConnectionOptions options)
    {
        options.Transports = HttpTransportType.WebSockets;
        options.SkipNegotiation = true;
    }

    private bool ShouldFallBackToNegotiation(Exception error, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return false;                 // the caller gave up
        if (Connection.State != HubConnectionState.Disconnected) return false;       // someone else is starting it
        return !IsFinalConnectFailure(error);
    }

    /// <summary>
    ///     A failure that negotiating cannot cure, looked for through the whole exception chain: the credentials were
    ///     refused (the second attempt would present the same ones), the hub itself refused the handshake (so the
    ///     transport worked), or the connection is disposed.
    /// </summary>
    private static bool IsFinalConnectFailure(Exception error)
    {
        if (error is HubException or ObjectDisposedException || IsAuthenticationFailure(error)) return true;

        if (error is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
                if (IsFinalConnectFailure(inner)) return true;
            return false;
        }

        return error.InnerException is { } cause && IsFinalConnectFailure(cause);
    }

    private static bool IsAuthenticationFailure(Exception error)
    {
#if NET5_0_OR_GREATER
        if (error is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }) return true;
#endif
        // A refused WebSocket upgrade has no status property to read; the status is in the message only:
        // "The server returned status code '401' when status code '101' was expected."
        return error is WebSocketException or HttpRequestException
               && (MentionsStatusCode(error.Message, "401") || MentionsStatusCode(error.Message, "403"));
    }

    /// <summary>True when <paramref name="code"/> stands in the message as a number of its own (not inside a port or a longer number).</summary>
    private static bool MentionsStatusCode(string message, string code)
    {
        for (var at = message.IndexOf(code, StringComparison.Ordinal); at >= 0; at = message.IndexOf(code, at + 1, StringComparison.Ordinal))
        {
            var digitBefore = at > 0 && char.IsDigit(message[at - 1]);
            var digitAfter = at + code.Length < message.Length && char.IsDigit(message[at + code.Length]);
            if (!digitBefore && !digitAfter) return true;
        }

        return false;
    }

    /// <summary>
    ///     Disconnects from the SignalR server.
    /// </summary>
    public virtual Task DisconnectAsync(CancellationToken cancellationToken = default)
        => Connection.StopAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return Connection?.DisposeAsync() ?? default;
    }
}
