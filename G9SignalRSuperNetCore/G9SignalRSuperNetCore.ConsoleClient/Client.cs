using G9SignalRSuperNetCore.Client;

/* ---------------------------------------------------------------
 *       Auto-generated client helper for hub 'CustomHub'
 * --------------------------------------------------------------- */

// Interface defining server-side methods callable from the client.
// Contains methods exposed by the server for client interaction.
public interface G9ICustomHubMethods
{
    /// <summary>
    ///     Information
    /// </summary>
    /// <param name="userName">user Name</param>
    /// <param name="password">Password</param>
    /// <returns>
    ///     Task
    /// </returns>
    Task Login(string userName, string password);

    Task Replay(string message);
}

// Interface defining client-side callback methods (listeners).
// Contains methods that the server can invoke on the client.
public interface G9ICustomHubListeners
{
    /// <summary>
    ///     Method to receive the result for login
    /// </summary>
    /// <param name="accepted">Specifies that access is true or not</param>
    Task LoginResult(bool accepted);

    Task Replay(string message);
}

// Client class for hub 'CustomHub'.
// Implements 'G9ICustomHubListeners' for handling server-side callbacks
// and provides methods to interact with the server via 'G9ICustomHubMethods'.
public class G9CCustomHubClient :
    G9SignalRSuperNetCoreClient<G9CCustomHubClient, G9ICustomHubMethods, G9ICustomHubListeners>, G9ICustomHubListeners
{
    public G9CCustomHubClient(string serverUrl)
        : base($"{serverUrl}/CustomHub")
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