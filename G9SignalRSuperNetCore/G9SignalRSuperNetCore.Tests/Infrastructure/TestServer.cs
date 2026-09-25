using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using G9SignalRSuperNetCore.Client;
using G9SignalRSuperNetCore.Client.MessagePack;
using G9SignalRSuperNetCore.Server;
using G9SignalRSuperNetCore.Server.MessagePack;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace G9SignalRSuperNetCore.Tests.Infrastructure;

public enum TestProtocol
{
    Json,
    MessagePack
}

/// <summary>A Kestrel server on a free loopback port hosting <see cref="TestHub" />, with a byte counter and an upload folder.</summary>
public sealed class TestServer : IAsyncDisposable
{
    /// <summary><see cref="TestHub" /> again, on an endpoint that refuses the WebSocket transport (server-sent events and long polling only).</summary>
    public const string NoWebSocketsPrefix = "/no-websockets";

    /// <summary><see cref="TestHub" /> again, on an endpoint mapped with <c>allowStatefulReconnects: true</c>.</summary>
    public const string StatefulPrefix = "/stateful";

    /// <summary>Everything under it is answered 401 before SignalR sees it.</summary>
    public const string UnauthorizedPrefix = "/unauthorized";

    /// <summary>Everything under it is answered 403 before SignalR sees it.</summary>
    public const string ForbiddenPrefix = "/forbidden";

    private readonly WebApplication _app;

    private TestServer(WebApplication app, WireCounter wire, ServerProbe probe, string root, string url)
    {
        _app = app;
        Wire = wire;
        Probe = probe;
        UploadRoot = root;
        BaseUrl = url;
    }

    public WireCounter Wire { get; }

    /// <summary>The requests the server received, and the switch that cuts its TCP connections.</summary>
    public ServerProbe Probe { get; }

    public string UploadRoot { get; }

    /// <summary>The server root. The generated client appends <see cref="TestHub.Route" /> itself.</summary>
    public string BaseUrl { get; }

    /// <summary>The running host's services, for tests that inspect singleton state such as the hub filter.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>A user id provider for tests: the <c>user</c> query-string value of the connection, or none.</summary>
    public static readonly Func<HubConnectionContext, string?> UserFromQuery = connection =>
        connection.GetHttpContext()?.Request.Query["user"].ToString() is { Length: > 0 } user ? user : null;

    /// <summary>
    ///     Starts a server; <paramref name="offerMessagePack" /> adds the MessagePack protocol next to JSON, and
    ///     <paramref name="statefulReconnectBufferSize" /> goes through <c>AddG9SignalRSuperNetCoreStatefulReconnect</c>.
    ///     <paramref name="configureServices"/> runs after the core services and before the file-upload registration;
    ///     <paramref name="configureApp"/> runs after the standard hubs are mapped.
    /// </summary>
    public static async Task<TestServer> StartAsync(
        bool offerMessagePack,
        long? statefulReconnectBufferSize = null,
        Action<IServiceCollection>? configureServices = null,
        Action<WebApplication>? configureApp = null,
        Func<HubConnectionContext, string?>? userIdentifier = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "g9signalr-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var wire = new WireCounter();
        var probe = new ServerProbe();

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0, listen =>
        {
            listen.Use(wire.Middleware);
            listen.Use(probe.ConnectionMiddleware);
        }));
        builder.Services.AddSignalRSuperNetCoreCore(userIdentifier);
        configureServices?.Invoke(builder.Services);
        builder.Services.AddG9SignalRSuperNetCoreFileUpload(options =>
        {
            options.RootDirectory = root;
            options.AckEveryNChunks = 4;
        });
        if (offerMessagePack) builder.Services.AddG9SignalRSuperNetCoreMessagePack(TestShapes.GeneratedTypeShapeProvider);
        if (statefulReconnectBufferSize is { } bufferSize) builder.Services.AddG9SignalRSuperNetCoreStatefulReconnect(bufferSize);

        var app = builder.Build();
        app.Use(probe.RequestMiddleware);
        app.Use((context, next) =>
        {
            var refusal = context.Request.Path.StartsWithSegments(UnauthorizedPrefix) ? StatusCodes.Status401Unauthorized
                : context.Request.Path.StartsWithSegments(ForbiddenPrefix) ? StatusCodes.Status403Forbidden
                : 0;
            if (refusal == 0) return next(context);
            context.Response.StatusCode = refusal;
            return Task.CompletedTask;
        });
        // Test users: `?roles=a,b` and `?claims=type:value,…` become an authenticated principal (only when present).
        app.Use((context, next) =>
        {
            var roles = context.Request.Query["roles"].ToString();
            var claims = context.Request.Query["claims"].ToString();
            if (roles.Length == 0 && claims.Length == 0) return next(context);
            var list = roles.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => new Claim(ClaimTypes.Role, r))
                .Concat(claims.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(c => c.Split(':', 2))
                    .Select(pair => new Claim(pair[0], pair.Length > 1 ? pair[1] : string.Empty)));
            context.User = new ClaimsPrincipal(new ClaimsIdentity(list, "test"));
            return next(context);
        });
        app.UseWebSockets();   // the slim builder leaves it out; without it the WebSocket transport gets a 404
        app.AddSignalRSuperNetCoreServerHub<TestHub, ITestHubClient>(TestHub.Route);
        app.AddSignalRSuperNetCoreServerHub<TestHub, ITestHubClient>(NoWebSocketsPrefix + TestHub.Route,
            options => options.Transports = HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling);
        app.AddSignalRSuperNetCoreServerHub<TestHub, ITestHubClient>(StatefulPrefix + TestHub.Route, allowStatefulReconnects: true);
        app.AddSignalRSuperNetCoreServerHub<PolicyHub, IPolicyHubClient>(PolicyHub.Route);
        app.AddSignalRSuperNetCoreServerHub<GuardedHub, IPolicyHubClient>(GuardedHub.Route);
        app.AddSignalRSuperNetCoreServerHub<RoleGuardedHub, IPolicyHubClient>(RoleGuardedHub.Route);
        configureApp?.Invoke(app);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new TestServer(app, wire, probe, root, address.TrimEnd('/'));
    }

    /// <summary>A started JSON <see cref="HubConnection"/> to <paramref name="pathAndQuery"/> (e.g. <c>/policy?user=alice</c>).</summary>
    public async Task<HubConnection> ConnectRawAsync(string pathAndQuery)
    {
        var connection = new HubConnectionBuilder().WithUrl(BaseUrl + pathAndQuery).Build();
        await connection.StartAsync();
        return connection;
    }

    public async Task<TestClient> ConnectAsync(TestProtocol protocol)
    {
        var client = new TestClient(BaseUrl, protocol);
        await client.ConnectAsync();
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        try
        {
            Directory.Delete(UploadRoot, recursive: true);
        }
        catch (IOException)
        {
            // A file handle may still be closing; the temp folder is cleaned by the OS eventually.
        }
    }
}

/// <summary>The source-generated typed client for <see cref="TestHub" />, on the chosen protocol.</summary>
public class TestClient : TestHubClient
{
    private readonly TaskCompletionSource<TestReading> _poked = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TestClient(string serverUrl, TestProtocol protocol)
        : base(serverUrl, customConfigureBuilder: protocol == TestProtocol.MessagePack
            ? builder => builder.AddG9MessagePackProtocol(TestShapes.GeneratedTypeShapeProvider)
            : null)
    {
        StateChanged += Phases.Enqueue;
    }

    /// <summary>Every lifecycle transition this client reported, in order.</summary>
    public ConcurrentQueue<G9DtConnectionState> Phases { get; } = new();

    public Task<TestReading> FirstPoke => _poked.Task;

    private long _acknowledged;

    /// <summary>The highest byte count the server acknowledged through the generated <c>UploadProgress</c> listener.</summary>
    public long Acknowledged => Interlocked.Read(ref _acknowledged);

    // The hub's listener interface names the SERVER DTO; the generated client exposes its client-library twin (2.6).
    public override Task UploadProgress(G9SignalRSuperNetCore.Client.FileUpload.G9DtUploadProgress progress)
    {
        long current;
        while (progress.BytesReceived > (current = Interlocked.Read(ref _acknowledged)))
            Interlocked.CompareExchange(ref _acknowledged, progress.BytesReceived, current);
        return Task.CompletedTask;
    }

    public override Task Poked(TestReading reading)
    {
        _poked.TrySetResult(reading);
        return Task.CompletedTask;
    }
}

/// <summary>
///     A <see cref="TestClient" /> that opts in to <see cref="G9DtClientConnectionOptions.WebSocketsFirst" /> the way an app
///     does: by overriding the hook. The hook runs inside the base constructor, so it reads nothing this class assigns.
/// </summary>
public sealed class WebSocketsFirstTestClient(string serverUrl, TestProtocol protocol) : TestClient(serverUrl, protocol)
{
    protected override void ConfigureConnectionOptions(G9DtClientConnectionOptions options) => options.WebSocketsFirst = true;
}

/// <summary>A <see cref="TestClient" /> that asks for stateful reconnect.</summary>
public sealed class StatefulTestClient(string serverUrl, TestProtocol protocol) : TestClient(serverUrl, protocol)
{
    protected override void ConfigureConnectionOptions(G9DtClientConnectionOptions options) => options.UseStatefulReconnect = true;
}

/// <summary>Both options on: stateful reconnect needs negotiation, so it must win over skipping it.</summary>
public sealed class StatefulWebSocketsFirstTestClient(string serverUrl, TestProtocol protocol) : TestClient(serverUrl, protocol)
{
    protected override void ConfigureConnectionOptions(G9DtClientConnectionOptions options)
    {
        options.UseStatefulReconnect = true;
        options.WebSocketsFirst = true;
    }
}
