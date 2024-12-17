using G9SignalRSuperNetCore.Client;
using Microsoft.AspNetCore.SignalR.Client;

namespace G9SignalRSuperNetCore.ConsoleClient;

public class G9CCustomHubClient : G9SignalRSuperNetCoreClient<MyInterface>
{
    public G9CCustomHubClient(string serverUrl)
        // 'CustomHub' is specified with method "RoutePattern"  
        : base($"{serverUrl}/CustomHub")
    {
        // Based on all methods specified in interface
        Connection.On<bool>(nameof(LoginResult), LoginResult);
    }

    public Task LoginResult(bool accepted)
    {

        return Task.CompletedTask;
    }
}