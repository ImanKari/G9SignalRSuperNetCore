using G9SignalRSuperNetCore.Sample.Shared;
using G9SignalRSuperNetCore.Server;

namespace G9SignalRSuperNetCore.WebServer;

/// <summary>
///     Sample SignalR server hosting <see cref="ChatHub"/> for end-to-end testing
///     against the Consolonia console test client.
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        // Demote SignalR's hub-dispatcher "Failed to invoke hub method" log entries to Warning.
        // We intentionally throw HubException with a stable error code (e.g. G9_RATE_LIMITED) when
        // a policy filter rejects a call — that's the documented SignalR pattern for sending an
        // error back to the client. SignalR logs every hub-method exception at fail/Error level by
        // default, which floods the console during rate-limit testing. Demoting to Warning keeps
        // real failures visible while quieting the policy-rejection noise.
        builder.Logging.AddFilter("Microsoft.AspNetCore.SignalR.Internal.DefaultHubDispatcher", LogLevel.Warning);

        // Core SignalR services + deny-by-default policy + custom UserIdProvider
        builder.Services.AddSignalRSuperNetCoreCore();

        // Resumable file upload service (writes to ./uploads/, partials in ./uploads/.partial/)
        builder.Services.AddG9SignalRSuperNetCoreFileUpload(opt =>
        {
            opt.RootDirectory = Path.Combine(builder.Environment.ContentRootPath, "uploads");
            opt.MaxBytes = 5L * 1024 * 1024 * 1024; // 5 GB cap
            opt.AckEveryNChunks = 16;               // ~1 MB between server-ack pushes at 64 KB chunks
        });

        var app = builder.Build();

        app.MapGet("/", () => "G9SignalRSuperNetCore — sample chat server is running.");
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

        app.AddSignalRSuperNetCoreServerHub<ChatHub, IChatClient>(routePattern: ChatHub.Route);

        app.Run();
    }
}
