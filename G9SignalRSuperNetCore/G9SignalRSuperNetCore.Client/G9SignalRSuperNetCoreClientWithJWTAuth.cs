using G9SignalRSuperNetCore.Client.Classes.DataTypes;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;

namespace G9SignalRSuperNetCore.Client;

/// <summary>
///     A SignalR client base class that adds JWT authentication.
///     The client first connects to a dedicated authentication route, exchanges credentials
///     for a JWT, and then connects to the protected hub using that token.
/// </summary>
/// <typeparam name="TTargetClass">The derived client type (CRTP self-type).</typeparam>
/// <typeparam name="TServerHubMethods">An interface defining methods the client can invoke on the server.</typeparam>
/// <typeparam name="TClientListenerMethods">An interface defining methods the server can invoke on the client.</typeparam>
public abstract class G9SignalRSuperNetCoreClientWithJWTAuth<TTargetClass, TServerHubMethods, TClientListenerMethods>
    : G9SignalRSuperNetCoreClient<TTargetClass, TServerHubMethods, TClientListenerMethods>
    where TTargetClass : G9SignalRSuperNetCoreClient<TTargetClass, TServerHubMethods, TClientListenerMethods>
    where TServerHubMethods : class
    where TClientListenerMethods : class
{
    private readonly HubConnection _authConnection;
    private readonly Action<HttpConnectionOptions>? _configureHttpConnection;
    private readonly Func<IHubConnectionBuilder, IHubConnectionBuilder>? _customConfigureBuilder;
    private readonly string? _jwToken;
    private readonly string _serverUrl;

    private string? _authJWToken;
    private TaskCompletionSource<G9DtAuthorizeResult>? _tcsAuthorizeResult;

    /// <summary>
    ///     Indicates whether the client has been authorized with a valid JWT.
    /// </summary>
    public bool IsAuthorized { get; private set; }

    /// <summary>
    ///     Initializes a JWT-authenticated client.
    /// </summary>
    protected G9SignalRSuperNetCoreClientWithJWTAuth(
        string serverUrl,
        string serverAuthUrl,
        string? jwToken = null,
        Func<IHubConnectionBuilder, IHubConnectionBuilder>? customConfigureBuilder = null,
        Func<IHubConnectionBuilder, IHubConnectionBuilder>? customConfigureBuilderForAuthServer = null,
        Action<HttpConnectionOptions>? configureHttpConnection = null,
        Action<HttpConnectionOptions>? configureHttpConnectionForAuthServer = null)
    {
        if (string.IsNullOrEmpty(serverUrl)) throw new ArgumentException("Value cannot be null or empty.", nameof(serverUrl));
        if (string.IsNullOrEmpty(serverAuthUrl)) throw new ArgumentException("Value cannot be null or empty.", nameof(serverAuthUrl));

        _serverUrl = serverUrl;
        _jwToken = jwToken;
        _customConfigureBuilder = customConfigureBuilder;
        _configureHttpConnection = configureHttpConnection;

        IHubConnectionBuilder authBuilder = new HubConnectionBuilder()
            .WithUrl(serverAuthUrl, options => configureHttpConnectionForAuthServer?.Invoke(options))
            .WithAutomaticReconnect();

        if (customConfigureBuilderForAuthServer != null)
            authBuilder = customConfigureBuilderForAuthServer(authBuilder);

        _authConnection = authBuilder.Build();
        _authConnection.ServerTimeout = TimeSpan.FromSeconds(60);

        _authConnection.On<G9DtAuthorizeResult>(nameof(AuthorizeResult), AuthorizeResult);
    }

    /// <summary>
    ///     Sends an authorization payload to the auth server and returns the result.
    /// </summary>
    public async Task<G9DtAuthorizeResult> AuthorizeAsync(object authorizeData, CancellationToken cancellationToken = default)
    {
        if (authorizeData is null) throw new ArgumentNullException(nameof(authorizeData));

        _tcsAuthorizeResult = new TaskCompletionSource<G9DtAuthorizeResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await _authConnection.StartAsync(cancellationToken).ConfigureAwait(false);
            await _authConnection.SendCoreAsync("Authorize", new[] { authorizeData }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _tcsAuthorizeResult.TrySetException(ex);
        }

        return await _tcsAuthorizeResult.Task.ConfigureAwait(false);
    }

    private async Task AuthorizeResult(G9DtAuthorizeResult authorize)
    {
        IsAuthorized = authorize.IsAccepted;
        if (authorize.IsAccepted) _authJWToken = authorize.JWToken;

        _tcsAuthorizeResult?.TrySetResult(authorize);

        try
        {
            await _authConnection.StopAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort stop; ignore errors during teardown.
        }
    }

    /// <inheritdoc />
    public override Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        PrepareConnection(_serverUrl, _customConfigureBuilder, configHttp =>
        {
            configHttp.AccessTokenProvider = () =>
            {
                if (!string.IsNullOrWhiteSpace(_jwToken)) return Task.FromResult<string?>(_jwToken);
                if (!string.IsNullOrWhiteSpace(_authJWToken)) return Task.FromResult<string?>(_authJWToken);
                throw new InvalidOperationException(
                    "JWT token is required for authentication. Call AuthorizeAsync first or pass a token to the constructor.");
            };
            _configureHttpConnection?.Invoke(configHttp);
        });

        return Connection.StartAsync(cancellationToken);
    }

    /// <summary>
    ///     Connects using an explicit JWT.
    /// </summary>
    public Task ConnectAsync(string jwToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(jwToken)) throw new ArgumentException("Value cannot be null or empty.", nameof(jwToken));

        PrepareConnection(_serverUrl, _customConfigureBuilder, configHttp =>
        {
            configHttp.AccessTokenProvider = () => Task.FromResult<string?>(jwToken);
            _configureHttpConnection?.Invoke(configHttp);
        });

        return Connection.StartAsync(cancellationToken);
    }
}
