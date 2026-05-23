using G9SignalRSuperNetCore.Server.Classes.Abstracts;

namespace G9SignalRSuperNetCore.WebServer;

/// <summary>
///     Sample unauthenticated hub.
/// </summary>
public class CustomHub : G9AHubBase<CustomHub, CustomClientInterface>
{
    public override string RoutePattern() => "/ApplicationHub";

    /// <summary>
    /// Login sample method.
    /// </summary>
    /// <param name="userName">User name</param>
    /// <param name="password">Password</param>
    public Task Login(string userName, string password) => Clients.Caller.LoginResult(true);

    /// <summary>
    /// Echoes the supplied message back to the caller.
    /// </summary>
    /// <param name="message">The message to echo.</param>
    public Task Replay(string message)
    {
        Console.WriteLine(Context.ConnectionId);
        return Clients.Caller.Replay(message);
    }
}
