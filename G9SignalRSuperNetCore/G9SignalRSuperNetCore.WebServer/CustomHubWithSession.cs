using G9SignalRSuperNetCore.Server.Classes.Abstracts;
using G9SignalRSuperNetCore.Server.Classes.Attributes;

namespace G9SignalRSuperNetCore.WebServer;

/// <summary>
///     Sample hub that uses an in-memory session store but no JWT auth.
/// </summary>
public class CustomHubWithSession : G9AHubBaseWithSession<CustomHubWithSession, CustomClientInterface, CustomHubSession>
{
    public override string RoutePattern() => "/CustomHubWithSession";

    public Task Login(string userName, string password) => Clients.Caller.LoginResult(true);

    [G9AttrExcludeFromClientGeneration]
    public Task Replay(string message) => Clients.Caller.Replay(message);
}
