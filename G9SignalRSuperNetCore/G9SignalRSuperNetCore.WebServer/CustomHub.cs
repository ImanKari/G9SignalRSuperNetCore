using G9SignalRSuperNetCore.Server.Classes.Abstracts;

namespace G9SignalRSuperNetCore.WebServer;

public class CustomHub : G9AHubBase<CustomHub, CustomClientInterface>
{
    public override string RoutePattern()
    {
        return "/CustomHub";
    }


    public async Task Login(string userName, string password)
    {
        await Clients.Caller.LoginResult(true);
    }
}

