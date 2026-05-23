using G9SignalRSuperNetCore.Sample.Shared;
using G9SignalRSuperNetCore.Server;

namespace G9SignalRSuperNetCore.WebServer;

/// <summary>
///     Sample SignalR server hosting <see cref="ChatHub"/> for end-to-end testing
///     against the console test client.
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        // Core SignalR services + deny-by-default policy + custom UserIdProvider
        builder.Services.AddSignalRSuperNetCoreCore();

        var app = builder.Build();

        app.MapGet("/", () => "G9SignalRSuperNetCore — sample chat server is running.");
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

        // Map the hub at its declared route pattern
        app.AddSignalRSuperNetCoreServerHub<ChatHub, IChatClient>(routePattern: ChatHub.Route);

        app.Run();
    }
}
