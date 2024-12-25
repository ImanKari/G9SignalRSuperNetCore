using G9SignalRSuperNetCore.Server.Classes.Abstracts;
using G9SignalRSuperNetCore.Server.Classes.Attributes;

namespace G9SignalRSuperNetCore.WebServer;

public class CustomHubWithSession : G9AHubBaseWithSession<CustomHubWithSession, CustomClientInterface, CustomHubSession>
{
    public override string RoutePattern()
    {
        return "/CustomHubWithSession";
    }


    public async Task Login(string userName, string password)
    {
        await Clients.Caller.LoginResult(true);
    }

    [G9AttrExcludeFromClientGeneration]
    public async Task Replay(string message)
    {
        await Clients.Caller.Replay(message);
    }
}