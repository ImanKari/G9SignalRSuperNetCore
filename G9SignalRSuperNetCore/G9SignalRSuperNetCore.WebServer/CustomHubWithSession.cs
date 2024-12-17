using G9SignalRSuperNetCore.Server.Classes.Abstracts;

namespace G9SignalRSuperNetCore.WebServer;

public class CustomHubWithSession : G9AHubBaseWithSession<CustomHubWithSession, CustomHubSession, CustomClientInterface>
{
    public override string RoutePattern()
    {
        return "/CustomHubWithSession";
    }


    public async Task Login(string userName, string password)
    {
        await Clients.Caller.LoginResult(true);
    }
}