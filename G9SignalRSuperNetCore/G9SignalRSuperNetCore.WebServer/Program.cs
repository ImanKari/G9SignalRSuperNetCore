using G9SignalRSuperNetCore.Server;

namespace G9SignalRSuperNetCore.WebServer;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();

        // Core SignalR services + deny policy + custom UserIdProvider
        builder.Services.AddSignalRSuperNetCoreCore();

        // Pluggable session store (in-memory by default; swap in Redis for scale-out)
        builder.Services.AddG9SignalRSuperNetCoreSessionStore<CustomHubSession>();

        // JWT authentication for the protected hub route
        builder.Services.AddSignalRSuperNetCoreJwt(
            hubPath: CustomHubWithJWTAuthAndSession.HubRoute,
            validationParameters: CustomHubWithJWTAuthAndSession.TokenValidationParameters);

        var app = builder.Build();

        app.MapGet("/", () => "G9SignalRSuperNetCore WebServer sample");

        // Map the JWT auth route + the protected hub
        app.AddSignalRSuperNetCoreJwtHub<CustomHubWithJWTAuthAndSession, CustomClientInterface>(
            hubRoutePattern: CustomHubWithJWTAuthAndSession.HubRoute,
            authRoutePattern: CustomHubWithJWTAuthAndSession.AuthRoute,
            authenticate: CustomHubWithJWTAuthAndSession.AuthenticateAsync);

        app.Run();
    }
}
