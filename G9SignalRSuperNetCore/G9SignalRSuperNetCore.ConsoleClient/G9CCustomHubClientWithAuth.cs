using G9SignalRSuperNetCore.Client;

/* ---------------------------------------------------------------
 *       Auto-generated client helper for hub 'CustomHub'
 * --------------------------------------------------------------- */

/// <summary>
///     Interface defining server-side methods callable from the client.
///     Contains methods exposed by the server for client interaction.
/// </summary>
public interface G9ICustomHubMethodsWithJWTAuth
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
public interface G9ICustomHubListenersWithJWTAuth
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
public class G9CCustomHubClientWithJWTAuth :
    G9SignalRSuperNetCoreClientWithJWTAuth<G9CCustomHubClientWithJWTAuth, G9ICustomHubMethodsWithJWTAuth, G9ICustomHubListenersWithJWTAuth>, G9ICustomHubListenersWithJWTAuth
{
    public G9CCustomHubClientWithJWTAuth(string serverUrl, string? jwToken = null)
        : base($"{serverUrl}/AuthHub", $"{serverUrl}/SecureHub", jwToken)
    {
    }

    public Task LoginResult(bool accepted)
    {
        Console.WriteLine(accepted);
        return Task.CompletedTask;
    }

    public Task Replay(string message)
    {
        Console.WriteLine($"Received Message: {message}");
        return Task.CompletedTask;
    }
}