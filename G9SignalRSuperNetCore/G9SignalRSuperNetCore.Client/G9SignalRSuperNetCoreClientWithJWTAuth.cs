using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;

namespace G9SignalRSuperNetCore.Client;

public abstract class
    G9SignalRSuperNetCoreClientWithJWTAuth<TTargetClass, TServerHubMethods, TClientListenerMethods> :
    G9SignalRSuperNetCoreClient<TTargetClass, TServerHubMethods, TClientListenerMethods>
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


    private Func<bool, string?, string?, Task>? _authResult;

    protected G9SignalRSuperNetCoreClientWithJWTAuth(
        string serverAuthUrl,
        string serverUrl,
        string? jwToken = null,
        Func<IHubConnectionBuilder, IHubConnectionBuilder>? customConfigureBuilder = null,
        Func<IHubConnectionBuilder, IHubConnectionBuilder>? customConfigureBuilderForAuthServer = null,
        Action<HttpConnectionOptions>? configureHttpConnection = null,
        Action<HttpConnectionOptions>? configureHttpConnectionForAuthServer = null)
    {
        _serverUrl = serverUrl;
        _jwToken = jwToken;
        _customConfigureBuilder = customConfigureBuilder;
        _configureHttpConnection = configureHttpConnection;

        var preCompile = new HubConnectionBuilder()
            .WithUrl(serverAuthUrl, options => { configureHttpConnectionForAuthServer?.Invoke(options); })
            .WithAutomaticReconnect();

        preCompile = customConfigureBuilderForAuthServer?.Invoke(preCompile) ?? preCompile;

        _authConnection = preCompile.Build();
        _authConnection.ServerTimeout = TimeSpan.FromSeconds(60); // Wait for server response for 60 seconds

        // Automatically wire up all methods from the TClientListenerMethods interface
        _authConnection.On<bool, string?, string?>(nameof(AuthorizeResult), AuthorizeResult);
    }

    /// <summary>
    ///     Spec
    /// </summary>
    public bool IsAuthorized { private set; get; }

    private async Task AuthorizeResult(bool isAccepted, string? reason, string? jwToken)
    {
        IsAuthorized = isAccepted;

        if (isAccepted)
            _authJWToken = jwToken;

        if (_authResult != null)
            await _authResult.Invoke(isAccepted, reason, jwToken);

        await _authConnection.StopAsync();
    }

    public async Task Authorize(object authorizeData, Func<bool, string?, string?, Task> resultCallBack)
    {
        _authResult = resultCallBack;
        await _authConnection.StartAsync();
        await _authConnection.SendCoreAsync("Authorize", [authorizeData]);
    }

    public new async Task ConnectAsync()
    {
        PrepareConnection(_serverUrl, _customConfigureBuilder, configHttp =>
        {
            configHttp.AccessTokenProvider = () =>
                !string.IsNullOrWhiteSpace(_jwToken)
                    ? Task.FromResult(_jwToken)
                    : Task.FromResult(_authJWToken);

            _configureHttpConnection?.Invoke(configHttp);
        });
        await Connection.StartAsync();
        Console.WriteLine("Connected to the server.");
    }

    public async Task ConnectAsync(string jwToken)
    {
        PrepareConnection(_serverUrl, _customConfigureBuilder, configHttp =>
        {
            configHttp.AccessTokenProvider = () => Task.FromResult(jwToken)!;

            _configureHttpConnection?.Invoke(configHttp);
        });
        await Connection.StartAsync();
    }
}