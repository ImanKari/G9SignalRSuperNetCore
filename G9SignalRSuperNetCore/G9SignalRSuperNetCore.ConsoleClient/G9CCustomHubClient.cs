﻿using G9SignalRSuperNetCore.Client;

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
    public G9CCustomHubClient(string serverUrl)
        : base($"{serverUrl}/ApplicationHub")
    {
    }

    public Task LoginResult(bool accepted)
    {
        Console.WriteLine(accepted);
        return Task.CompletedTask;
    }

    public async Task Replay(string message)
    {
        Console.WriteLine(message);
        var value = int.Parse(message) + 1;
        await Server.Replay(value.ToString());
    }
}