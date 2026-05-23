using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;

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
        ArgumentException.ThrowIfNullOrEmpty(serverUrl);
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
    ///     Builds and stores the underlying <see cref="HubConnection"/>.
    /// </summary>
    protected internal void PrepareConnection(
        string serverUrl,
        Func<IHubConnectionBuilder, IHubConnectionBuilder>? customConfigureBuilder = null,
        Action<HttpConnectionOptions>? configureHttpConnection = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(serverUrl);

        IHubConnectionBuilder builder = new HubConnectionBuilder()
            .WithUrl(serverUrl, options => configureHttpConnection?.Invoke(options))
            .WithAutomaticReconnect(new G9CClientReconnectPolicy());

        if (customConfigureBuilder != null) builder = customConfigureBuilder(builder);

        Connection = builder.Build();
        Connection.ServerTimeout = TimeSpan.FromSeconds(60);

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
    ///     Connects to the SignalR server.
    /// </summary>
    public virtual async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        StateChanged?.Invoke(new G9DtConnectionState(G9EConnectionPhase.Connecting, DateTime.UtcNow, null));
        try
        {
            await Connection.StartAsync(cancellationToken).ConfigureAwait(false);
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
    ///     Disconnects from the SignalR server.
    /// </summary>
    public virtual Task DisconnectAsync(CancellationToken cancellationToken = default)
        => Connection.StopAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return Connection?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
