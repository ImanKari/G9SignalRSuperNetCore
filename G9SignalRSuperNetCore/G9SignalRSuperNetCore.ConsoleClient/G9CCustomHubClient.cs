//using G9SignalRSuperNetCore.Client;
//using G9SignalRSuperNetCore.Common.Interfaces;
//using Microsoft.AspNetCore.SignalR.Client;

//namespace G9SignalRSuperNetCore.ConsoleClient;

//public interface ClientMethod : G9IClientListener
//{
//    public Task MethodA(int a);
//}

//public class G9CCustomHubClient : G9SignalRSuperNetCoreClient<G9CCustomHubClient, MyInterface, ClientMethod>, ClientMethod
//{
//    public G9CCustomHubClient(string serverUrl)
//        // 'CustomHub' is specified with method "RoutePattern"  
//        : base($"{serverUrl}/CustomHub")
//    {
//        // Based on all methods specified in interface
//        Connection.On<bool>(nameof(LoginResult), LoginResult);
//    }

//    public Task LoginResult(bool accepted)
//    {

//        return Task.CompletedTask;
//    }

//    public Task MethodA(int a)
//    {
//        throw new NotImplementedException();
//    }
//}