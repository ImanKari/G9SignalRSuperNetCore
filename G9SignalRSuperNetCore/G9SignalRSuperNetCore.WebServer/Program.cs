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

        // Live log feed: register the shared backing store and the ILoggerProvider that funnels
        // every formatted entry into it. The /logs page subscribes through SignalR streaming and
        // renders entries as they're produced.
        var logFeed = new G9CServerLogFeed();
        builder.Services.AddSingleton(logFeed);
        builder.Logging.AddProvider(new G9CServerLogProvider(logFeed));

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

        // Bundle 3: groups + presence (opt-in; both are zero-cost when not registered).
        builder.Services.AddG9SignalRSuperNetCoreGroups<ChatHub>();
        builder.Services.AddG9SignalRSuperNetCorePresence();
        builder.Services.AddHostedService<G9CPresenceBroadcastService>();

        // Bundle 5: app-level encryption (P-256 ECDH + HKDF + ChaCha20-Poly1305).
        builder.Services.AddG9SignalRSuperNetCoreHandshake();

        var app = builder.Build();

        app.MapGet("/", () => "G9SignalRSuperNetCore — sample chat server is running. Live logs: /logs");
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

        // Live-log page (HTML) + the streaming log hub it subscribes to.
        app.MapGet("/logs", () => Results.Content(G9CServerLogPage.Html, "text/html"));
        app.MapHub<G9CServerLogHub>(G9CServerLogHub.Route);

        app.AddSignalRSuperNetCoreServerHub<ChatHub, IChatClient>(routePattern: ChatHub.Route);

        app.Run();
    }
}
