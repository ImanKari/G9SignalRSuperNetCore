using G9SignalRSuperNetCore.Client.Classes.DataTypes;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;

namespace G9SignalRSuperNetCore.Client;

/// <summary>
///     A SignalR client class that adds JWT (JSON Web Token) authentication capabilities.
///     This class extends the base
///     <see
///         cref="G9SignalRSuperNetCoreClientWithJWTAuth{TTargetClass, TServerHubMethods, TClientListenerMethods}" />
///     by allowing connections with JWT tokens for authentication before connecting to the server hub.
/// </summary>
/// <typeparam name="TTargetClass">
///     The type of the target class inheriting from this client, which should also use JWT authentication.
/// </typeparam>
/// <typeparam name="TServerHubMethods">
///     An interface that defines the methods which can be invoked on the server hub.
/// </typeparam>
/// <typeparam name="TClientListenerMethods">
///     An interface that defines the methods which can be invoked by the server, typically server-to-client notifications.
/// </typeparam>
public abstract class G9SignalRSuperNetCoreClientWithJWTAuth<TTargetClass, TServerHubMethods, TClientListenerMethods> :
    G9SignalRSuperNetCoreClient<TTargetClass, TServerHubMethods, TClientListenerMethods>
    where TTargetClass : G9SignalRSuperNetCoreClient<TTargetClass, TServerHubMethods, TClientListenerMethods>
    where TServerHubMethods : class
    where TClientListenerMethods : class
{
    #region Constructor

    /// <summary>
    ///     Initializes a new instance of the
    ///     <see
    ///         cref="G9SignalRSuperNetCoreClientWithJWTAuth{TTargetClass, TServerHubMethods, TClientListenerMethods}" />
    ///     class with optional JWT authentication.
    /// </summary>
    /// <param name="serverUrl">The URL of the SignalR server.</param>
    /// <param name="serverAuthUrl">The URL of the authentication server.</param>
    /// <param name="jwToken">An optional JWT token for authenticating the connection.</param>
    /// <param name="customConfigureBuilder">An optional function to customize the SignalR connection configuration.</param>
    /// <param name="customConfigureBuilderForAuthServer">
    ///     An optional function to customize the authentication server
    ///     connection configuration.
    /// </param>
    /// <param name="configureHttpConnection">An optional action to configure HTTP connection options for the main connection.</param>
    /// <param name="configureHttpConnectionForAuthServer">
    ///     An optional action to configure HTTP connection options for the
    ///     authentication server.
    /// </param>
    protected G9SignalRSuperNetCoreClientWithJWTAuth(
        string serverUrl,
        string serverAuthUrl,
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

        // Build the HubConnection for authentication server
        var preCompile = new HubConnectionBuilder()
            .WithUrl(serverAuthUrl, options => { configureHttpConnectionForAuthServer?.Invoke(options); })
            .WithAutomaticReconnect();

        preCompile = customConfigureBuilderForAuthServer?.Invoke(preCompile) ?? preCompile;

        _authConnection = preCompile.Build();
        _authConnection.ServerTimeout = TimeSpan.FromSeconds(60); // Wait for server response for 60 seconds

        // Register the AuthorizationResult method to handle authorization responses
        _authConnection.On<G9DtAuthorizeResult>(nameof(AuthorizeResult), AuthorizeResult);
    }

    #endregion

    #region Properties

    /// <summary>
    ///     Indicates whether the client is authorized with a valid JWT token.
    ///     This property will be set to true if authorization is successful.
    /// </summary>
    public bool IsAuthorized { private set; get; }

    #endregion

    #region Fields

    /// <summary>
    ///     The <see cref="HubConnection" /> used for connecting to the authentication server.
    ///     This connection is responsible for communicating with the authentication server to handle the JWT authentication.
    /// </summary>
    private readonly HubConnection _authConnection;

    /// <summary>
    ///     Optional delegate to customize HTTP connection options for the main SignalR connection.
    /// </summary>
    private readonly Action<HttpConnectionOptions>? _configureHttpConnection;

    /// <summary>
    ///     Optional delegate to customize HTTP connection options for the authentication server.
    /// </summary>
    private readonly Func<IHubConnectionBuilder, IHubConnectionBuilder>? _customConfigureBuilder;

    /// <summary>
    ///     The JWT token passed to the client (if any) for authenticating the client connection.
    /// </summary>
    private readonly string? _jwToken;

    /// <summary>
    ///     The base server URL to which the SignalR connection will be established.
    /// </summary>
    private readonly string _serverUrl;

    /// <summary>
    ///     The JWT token used for authentication after the authorization process is completed.
    ///     This token will be set after a successful authorization from the authentication server.
    /// </summary>
    private string? _authJWToken;

    private TaskCompletionSource<G9DtAuthorizeResult>? _tcsAuthorizeResult;

    #endregion

    #region Authorization Methods

    /// <summary>
    ///     Handles the result of the authorization process by processing the response from the authentication server.
    ///     This method is automatically invoked when the authentication server responds to the authorization request.
    /// </summary>
    /// <param name="authorize">
    ///     An object of type <see cref="G9DtAuthorizeResult" /> that contains the result of the authorization process,
    ///     including whether the authorization was accepted, the JWT token if accepted, and any additional data from the
    ///     server.
    /// </param>
    private async Task AuthorizeResult(G9DtAuthorizeResult authorize)
    {
        // Set the authorization status based on the result
        IsAuthorized = authorize.IsAccepted;

        // If authorized, store the JWT token for future use
        if (authorize.IsAccepted)
            _authJWToken = authorize.JWToken;

        // Invoke the callback function, if defined, with the authorization result details
        if (_tcsAuthorizeResult != null)
            _tcsAuthorizeResult.SetResult(authorize);

        // Stop the authentication connection after processing the result
        await _authConnection.StopAsync();
    }

    /// <summary>
    ///     Initiates the authorization process with the authentication server.
    ///     This method sends the provided authorization data to the authentication server and waits for a response.
    /// </summary>
    /// <param name="authorizeData">The data to send to the authentication server for authorization.</param>
    /// <returns>
    ///     A <see cref="Task{TResult}" /> representing the asynchronous operation,
    ///     with the result being a <see cref="G9DtAuthorizeResult" /> object containing the details of the authorization
    ///     result.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the connection is not properly initialized or the JWT token is missing during the connection process.
    /// </exception>
    /// <exception cref="Exception">
    ///     Thrown if there is an issue with starting the connection or sending the authorization request.
    /// </exception>
    public async Task<G9DtAuthorizeResult> AuthorizeAsync(object authorizeData)
    {
        _tcsAuthorizeResult = new TaskCompletionSource<G9DtAuthorizeResult>();

        try
        {
            // Start the connection to the authentication server
            await _authConnection.StartAsync();

            // Send the authorization request
            await _authConnection.SendCoreAsync("Authorize", new[] { authorizeData });
        }
        catch (Exception ex)
        {
            _tcsAuthorizeResult.SetException(ex);
        }

        // Wait for the authorization result from the callback
        return await _tcsAuthorizeResult.Task;
    }

    #endregion

    #region Connection Methods

    /// <summary>
    ///     Establishes the SignalR connection to the main server using JWT token authentication.
    ///     This method uses either a previously provided JWT token or the token obtained during the authorization process.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the asynchronous operation of connecting to the server.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the JWT token is missing during the connection process.
    /// </exception>
    public new async Task ConnectAsync()
    {
        PrepareConnection(_serverUrl, _customConfigureBuilder, configHttp =>
        {
            // Set the access token provider to use either the stored JWT or the authenticated token
            configHttp.AccessTokenProvider = () =>
            {
                if (!string.IsNullOrWhiteSpace(_jwToken))
                    return Task.FromResult(_jwToken)!;
                if (!string.IsNullOrWhiteSpace(_authJWToken))
                    return Task.FromResult(_authJWToken)!;
                throw new InvalidOperationException(
                    "JWT token is required for authentication, but neither the provided token nor the authenticated token are available.");
            };

            _configureHttpConnection?.Invoke(configHttp); // Apply any additional HTTP connection configurations
        });

        // Start the connection to the SignalR server
        await Connection.StartAsync();
        Console.WriteLine("Connected to the server.");
    }

    /// <summary>
    ///     Establishes the SignalR connection to the main server using a specified JWT token for authentication.
    ///     This method allows the client to manually pass a JWT token for the connection process.
    /// </summary>
    /// <param name="jwToken">The JWT token to use for authenticating the connection.</param>
    /// <returns>A <see cref="Task" /> representing the asynchronous operation of connecting to the server.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the provided JWT token is null or empty.
    /// </exception>
    public async Task ConnectAsync(string jwToken)

    {
        PrepareConnection(_serverUrl, _customConfigureBuilder, configHttp =>
        {
            // Set the access token provider to use the provided JWT token
            configHttp.AccessTokenProvider = () => (Task.FromResult(jwToken)
                                                    ?? throw new InvalidOperationException(
                                                        "JWT token is required for authentication, but neither the provided token nor the authenticated token are available."))
                !;

            _configureHttpConnection?.Invoke(configHttp); // Apply any additional HTTP connection configurations
        });

        // Start the connection to the SignalR server
        await Connection.StartAsync();
        Console.WriteLine("Connected to the server.");
    }

    #endregion
}