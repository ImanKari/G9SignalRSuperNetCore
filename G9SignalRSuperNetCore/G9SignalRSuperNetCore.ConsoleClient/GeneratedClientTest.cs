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
public interface ICustomHubMethods
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
public interface ICustomHubListeners
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
///     Implements 'ICustomHubListeners' for handling server-side callbacks and provides methods to interact with the
///     server via 'ICustomHubMethods'.
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
public class CustomHubClient : G9SignalRSuperNetCoreClient<CustomHubClient, ICustomHubMethods, ICustomHubListeners>,
    ICustomHubListeners
{
    public CustomHubClient(string serverUrl,
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
public interface ICustomHubWithJWTAuthMethodsWithJWTAuth
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
public interface ICustomHubWithJWTAuthListenersWithJWTAuth
{
    /// <summary>
    ///     Method to receive the result for login
    /// </summary>
    /// <param name="accepted">Specifies that access is true or not</param>
    Task LoginResult(bool accepted);

    Task Replay(string message);
}

// The data type used for authentication (e.g., credentials, token information).
// This is a type introduced to support flexible authentication methods.
/// <summary>
///     Authentication type
/// </summary>
public class CustomHubWithJWTAuthCAuthenticationType

{
    /// <summary>
    ///     Identity
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    ///     Name
    /// </summary>
    public string Name { get; set; }

    public string Description { get; set; }
}


/// <summary>
///     Client class for interacting with the SignalR hub 'CustomHubWithJWTAuth'.
///     Implements 'ICustomHubWithJWTAuthListenersWithJWTAuth' for handling server-side callbacks and provides methods to
///     interact with the server via 'ICustomHubWithJWTAuthMethodsWithJWTAuth'.
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
public class CustomHubWithJWTAuthClientWithJWTAuth :
    G9SignalRSuperNetCoreClientWithJWTAuth<CustomHubWithJWTAuthClientWithJWTAuth,
        ICustomHubWithJWTAuthMethodsWithJWTAuth, ICustomHubWithJWTAuthListenersWithJWTAuth,
        CustomHubWithJWTAuthCAuthenticationType>, ICustomHubWithJWTAuthListenersWithJWTAuth
{
    public CustomHubWithJWTAuthClientWithJWTAuth(string serverUrl, string? jwToken = null,
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
public interface ICustomHubWithJWTAuthAndSessionMethodsWithJWTAuth
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
public interface ICustomHubWithJWTAuthAndSessionListenersWithJWTAuth
{
    /// <summary>
    ///     Method to receive the result for login
    /// </summary>
    /// <param name="accepted">Specifies that access is true or not</param>
    Task LoginResult(bool accepted);

    Task Replay(string message);
}

// The data type used for authentication (e.g., credentials, token information).
// This is a type introduced to support flexible authentication methods.
/// <summary>
///     Authentication type
/// </summary>
public class CustomHubWithJWTAuthAndSessionCAuthenticationType

{
    /// <summary>
    ///     Identity
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    ///     Name
    /// </summary>
    public string Name { get; set; }

    public string Description { get; set; }
}


/// <summary>
///     Client class for interacting with the SignalR hub 'CustomHubWithJWTAuthAndSession'.
///     Implements 'ICustomHubWithJWTAuthAndSessionListenersWithJWTAuth' for handling server-side callbacks and provides
///     methods to interact with the server via 'ICustomHubWithJWTAuthAndSessionMethodsWithJWTAuth'.
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
public class CustomHubWithJWTAuthAndSessionClientWithJWTAuth :
    G9SignalRSuperNetCoreClientWithJWTAuth<CustomHubWithJWTAuthAndSessionClientWithJWTAuth,
        ICustomHubWithJWTAuthAndSessionMethodsWithJWTAuth, ICustomHubWithJWTAuthAndSessionListenersWithJWTAuth,
        CustomHubWithJWTAuthAndSessionCAuthenticationType>, ICustomHubWithJWTAuthAndSessionListenersWithJWTAuth
{
    public CustomHubWithJWTAuthAndSessionClientWithJWTAuth(string serverUrl, string? jwToken = null,
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
public interface ICustomHubWithSessionMethods
{
    Task Login(string userName, string password);
}

/// <summary>
///     Interface defining client-side callback methods (listeners).
///     Contains methods that the server can invoke on the client.
/// </summary>
public interface ICustomHubWithSessionListeners
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
///     Implements 'ICustomHubWithSessionListeners' for handling server-side callbacks and provides methods to interact
///     with the server via 'ICustomHubWithSessionMethods'.
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
public class CustomHubWithSessionClient :
    G9SignalRSuperNetCoreClient<CustomHubWithSessionClient, ICustomHubWithSessionMethods,
        ICustomHubWithSessionListeners>, ICustomHubWithSessionListeners
{
    public CustomHubWithSessionClient(string serverUrl,
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