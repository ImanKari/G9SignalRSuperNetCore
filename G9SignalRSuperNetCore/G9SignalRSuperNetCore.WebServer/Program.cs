using G9SignalRSuperNetCore.Server;

namespace G9SignalRSuperNetCore.WebServer;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // SignalR Super Net Core Server Service
        builder.Services.AddSignalRSuperNetCoreServerService<CustomHub, CustomClientInterface>();

        builder.Services.AddSignalR(options => { });

#if DEBUG
        builder.Logging.AddConsole();
#endif

        var app = builder.Build();

        app.MapGet("/", () => "Hello World!");

        //app.MapHub<G9SignalRSuperNetCoreServerHub>("/aa", options => { });
        app.AddSignalRSuperNetCoreServerHub<CustomHub, CustomClientInterface>();
        //app.AddSignalRSuperNetCoreServerHub<CustomHubWithSession, CustomClientInterface>();

        

        app.Run();
    }
}
