using G9SignalRSuperNetCore.Server.Classes.Abstracts;

namespace G9SignalRSuperNetCore.WebServer
{
    public class CustomHubSession : G9ASession
    {
        protected override Task OnDisconnected(Exception? exception)
        {
            
            return Task.CompletedTask;
        }

        protected override Task OnConnectedAsync()
        {
            
            return Task.CompletedTask;
        }

        protected override Task OnDisposeAsync()
        {

            return Task.CompletedTask;
        }
    }
}
