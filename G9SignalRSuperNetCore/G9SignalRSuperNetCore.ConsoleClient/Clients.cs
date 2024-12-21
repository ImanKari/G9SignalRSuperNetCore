using G9SignalRSuperNetCore.Client;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;

/* ---------------------------------------------------------------
 *       Auto-generated client helper for hub 'CustomHub'
 * --------------------------------------------------------------- */

/// <summary>
///     Interface defining server-side methods callable from the client.
///     Contains methods exposed by the server for client interaction.
/// </summary>
public interface G9ICustomHubMethods
{
    /// <summary>
    ///     Information
    /// </summary>
    /// <param name="userName">user Name</param>
    /// <param name="password">Password</param>
    Task Login(string userName, string password);

    /// <summary>
    ///     Replay
    /// </summary>
    /// <param name="message">Okay</param>
    Task Replay(string message);
}

/// <summary>
///     Interface defining client-side callback methods (listeners).
///     Contains methods that the server can invoke on the client.
/// </summary>
public interface G9ICustomHubListeners
{
    /// <summary>
    ///     Method to receive the result for login
    /// </summary>
    /// <param name="accepted">Specifies that access is true or not</param>
    Task LoginResult(bool accepted);

    Task Replay(string message);
}

/// <summary>
///     Client class for interacting with the SignalR hub 'CustomHub'.
///     Implements 'G9ICustomHubListeners' for handling server-side callbacks and provides methods to interact with the
///     server via 'G9ICustomHubMethods' .
///     This class handles server-side method calls and processes callbacks from the server.
///     <para />
///     Sample:
///     <code>
/// public Task YourClientListenerMethods(ParamType YourParam, ...){
///     ...
///     // Calls server method if needed:
///     Server.YourServerMethod(ParamType YourParam, .....)
///     ...
/// }
/// </code>
/// </summary>
public class G9CCustomHubClient :
    G9SignalRSuperNetCoreClient<G9CCustomHubClient, G9ICustomHubMethods, G9ICustomHubListeners>, G9ICustomHubListeners
{
    public G9CCustomHubClient(string serverUrl,
        Func<IHubConnectionBuilder, IHubConnectionBuilder>? customConfigureBuilder = null,
        Action<HttpConnectionOptions>? configureHttpConnection = null)
        : base($"{serverUrl}/ApplicationHub", customConfigureBuilder, configureHttpConnection)
    {
    }

    public Task LoginResult(bool accepted)
    {
        Console.WriteLine($"Method: {nameof(LoginResult)}, Result: {accepted}");
        return Task.CompletedTask;
    }

    public Task Replay(string message)
    {
        Console.WriteLine($"Method: {nameof(Replay)}, Result: {message}");
        return Task.CompletedTask;
    }
}

/* ---------------------------------------------------------------
 *       Auto-generated client helper for hub 'CustomHubWithJWTAuth'
 * --------------------------------------------------------------- */

/// <summary>
///     Interface defining server-side methods callable from the client.
///     Contains methods exposed by the server for client interaction.
/// </summary>
public interface G9ICustomHubWithJWTAuthMethodsWithJWTAuth
{
    /// <summary>
    ///     Information
    /// </summary>
    /// <param name="userName">user Name</param>
    /// <param name="password">Password</param>
    Task Login(string userName, string password);

    /// <summary>
    ///     Replay
    /// </summary>
    /// <param name="message">Okay</param>
    Task Replay(string message);
}

/// <summary>
///     Interface defining client-side callback methods (listeners).
///     Contains methods that the server can invoke on the client.
/// </summary>
public interface G9ICustomHubWithJWTAuthListenersWithJWTAuth
{
    /// <summary>
    ///     Method to receive the result for login
    /// </summary>
    /// <param name="accepted">Specifies that access is true or not</param>
    Task LoginResult(bool accepted);

    Task Replay(string message);
}

/// <summary>
///     Client class for interacting with the SignalR hub 'CustomHubWithJWTAuth'.
///     Implements 'G9ICustomHubWithJWTAuthListenersWithJWTAuth' for handling server-side callbacks and provides methods to
///     interact with the server via 'G9ICustomHubWithJWTAuthMethodsWithJWTAuth' .
///     This class handles server-side method calls and processes callbacks from the server.
///     <para />
///     Sample:
///     <code>
/// public Task YourClientListenerMethods(ParamType YourParam, ...){
///     ...
///     // Calls server method if needed:
///     Server.YourServerMethod(ParamType YourParam, .....)
///     ...
/// }
/// </code>
/// </summary>
public class G9CCustomHubWithJWTAuthClientWithJWTAuth :
    G9SignalRSuperNetCoreClientWithJWTAuth<G9CCustomHubWithJWTAuthClientWithJWTAuth,
        G9ICustomHubWithJWTAuthMethodsWithJWTAuth, G9ICustomHubWithJWTAuthListenersWithJWTAuth>,
    G9ICustomHubWithJWTAuthListenersWithJWTAuth
{
    public G9CCustomHubWithJWTAuthClientWithJWTAuth(string serverUrl, string? jwToken = null,
        Func<IHubConnectionBuilder, IHubConnectionBuilder>? customConfigureBuilder = null,
        Func<IHubConnectionBuilder, IHubConnectionBuilder>? customConfigureBuilderForAuthServer = null,
        Action<HttpConnectionOptions>? configureHttpConnection = null,
        Action<HttpConnectionOptions>? configureHttpConnectionForAuthServer = null)
        : base($"{serverUrl}/SecureHub", $"{serverUrl}/AuthHub", jwToken, customConfigureBuilder,
            customConfigureBuilderForAuthServer, configureHttpConnection, configureHttpConnectionForAuthServer)
    {
    }

    public Task LoginResult(bool accepted)
    {
        Console.WriteLine($"Method: {nameof(LoginResult)}, Result: {accepted}");
        return Task.CompletedTask;
    }

    public Task Replay(string message)
    {
        Console.WriteLine($"Method: {nameof(Replay)}, Result: {message}");
        return Task.CompletedTask;
    }
}

/* ---------------------------------------------------------------
 *       Auto-generated client helper for hub 'CustomHubWithJWTAuthAndSession'
 * --------------------------------------------------------------- */

/// <summary>
///     Interface defining server-side methods callable from the client.
///     Contains methods exposed by the server for client interaction.
/// </summary>
public interface G9ICustomHubWithJWTAuthAndSessionMethodsWithJWTAuth
{
    /// <summary>
    ///     Information
    /// </summary>
    /// <param name="userName">user Name</param>
    /// <param name="password">Password</param>
    Task Login(string userName, string password);

    /// <summary>
    ///     Replay
    /// </summary>
    /// <param name="message">Okay</param>
    Task Replay(string message);
}

/// <summary>
///     Interface defining client-side callback methods (listeners).
///     Contains methods that the server can invoke on the client.
/// </summary>
public interface G9ICustomHubWithJWTAuthAndSessionListenersWithJWTAuth
{
    /// <summary>
    ///     Method to receive the result for login
    /// </summary>
    /// <param name="accepted">Specifies that access is true or not</param>
    Task LoginResult(bool accepted);

    Task Replay(string message);
}

/// <summary>
///     Client class for interacting with the SignalR hub 'CustomHubWithJWTAuthAndSession'.
///     Implements 'G9ICustomHubWithJWTAuthAndSessionListenersWithJWTAuth' for handling server-side callbacks and provides
///     methods to interact with the server via 'G9ICustomHubWithJWTAuthAndSessionMethodsWithJWTAuth' .
///     This class handles server-side method calls and processes callbacks from the server.
///     <para />
///     Sample:
///     <code>
/// public Task YourClientListenerMethods(ParamType YourParam, ...){
///     ...
///     // Calls server method if needed:
///     Server.YourServerMethod(ParamType YourParam, .....)
///     ...
/// }
/// </code>
/// </summary>
public class G9CCustomHubWithJWTAuthAndSessionClientWithJWTAuth :
    G9SignalRSuperNetCoreClientWithJWTAuth<G9CCustomHubWithJWTAuthAndSessionClientWithJWTAuth,
        G9ICustomHubWithJWTAuthAndSessionMethodsWithJWTAuth, G9ICustomHubWithJWTAuthAndSessionListenersWithJWTAuth>,
    G9ICustomHubWithJWTAuthAndSessionListenersWithJWTAuth
{
    public G9CCustomHubWithJWTAuthAndSessionClientWithJWTAuth(string serverUrl, string? jwToken = null,
        Func<IHubConnectionBuilder, IHubConnectionBuilder>? customConfigureBuilder = null,
        Func<IHubConnectionBuilder, IHubConnectionBuilder>? customConfigureBuilderForAuthServer = null,
        Action<HttpConnectionOptions>? configureHttpConnection = null,
        Action<HttpConnectionOptions>? configureHttpConnectionForAuthServer = null)
        : base($"{serverUrl}/SecureHub", $"{serverUrl}/AuthHub", jwToken, customConfigureBuilder,
            customConfigureBuilderForAuthServer, configureHttpConnection, configureHttpConnectionForAuthServer)
    {
    }

    public Task LoginResult(bool accepted)
    {
        Console.WriteLine($"Method: {nameof(LoginResult)}, Result: {accepted}");
        return Task.CompletedTask;
    }

    public Task Replay(string message)
    {
        Console.WriteLine($"Method: {nameof(Replay)}, Result: {message}");
        return Task.CompletedTask;
    }
}

/* ---------------------------------------------------------------
 *       Auto-generated client helper for hub 'CustomHubWithSession'
 * --------------------------------------------------------------- */

/// <summary>
///     Interface defining server-side methods callable from the client.
///     Contains methods exposed by the server for client interaction.
/// </summary>
public interface G9ICustomHubWithSessionMethods
{
}

/// <summary>
///     Interface defining client-side callback methods (listeners).
///     Contains methods that the server can invoke on the client.
/// </summary>
public interface G9ICustomHubWithSessionListeners
{
    /// <summary>
    ///     Method to receive the result for login
    /// </summary>
    /// <param name="accepted">Specifies that access is true or not</param>
    Task LoginResult(bool accepted);

    Task Replay(string message);
}

/// <summary>
///     Client class for interacting with the SignalR hub 'CustomHubWithSession'.
///     Implements 'G9ICustomHubWithSessionListeners' for handling server-side callbacks and provides methods to interact
///     with the server via 'G9ICustomHubWithSessionMethods' .
///     This class handles server-side method calls and processes callbacks from the server.
///     <para />
///     Sample:
///     <code>
/// public Task YourClientListenerMethods(ParamType YourParam, ...){
///     ...
///     // Calls server method if needed:
///     Server.YourServerMethod(ParamType YourParam, .....)
///     ...
/// }
/// </code>
/// </summary>
public class G9CCustomHubWithSessionClient :
    G9SignalRSuperNetCoreClient<G9CCustomHubWithSessionClient, G9ICustomHubWithSessionMethods,
        G9ICustomHubWithSessionListeners>, G9ICustomHubWithSessionListeners
{
    public G9CCustomHubWithSessionClient(string serverUrl,
        Func<IHubConnectionBuilder, IHubConnectionBuilder>? customConfigureBuilder = null,
        Action<HttpConnectionOptions>? configureHttpConnection = null)
        : base($"{serverUrl}/CustomHubWithSession", customConfigureBuilder, configureHttpConnection)
    {
    }

    public Task LoginResult(bool accepted)
    {
        Console.WriteLine($"Method: {nameof(LoginResult)}, Result: {accepted}");
        return Task.CompletedTask;
    }

    public Task Replay(string message)
    {
        Console.WriteLine($"Method: {nameof(Replay)}, Result: {message}");
        return Task.CompletedTask;
    }
}