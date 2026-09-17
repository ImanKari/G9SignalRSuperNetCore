using System.Net;
using G9SignalRSuperNetCore.Client.MessagePack;
using G9SignalRSuperNetCore.Server;
using G9SignalRSuperNetCore.Server.MessagePack;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
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
    private readonly WebApplication _app;

    private TestServer(WebApplication app, WireCounter wire, string root, string url)
    {
        _app = app;
        Wire = wire;
        UploadRoot = root;
        BaseUrl = url;
    }

    public WireCounter Wire { get; }

    public string UploadRoot { get; }

    /// <summary>The server root. The generated client appends <see cref="TestHub.Route" /> itself.</summary>
    public string BaseUrl { get; }

    /// <summary>The running host's services, for tests that inspect singleton state such as the hub filter.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>Starts a server; <paramref name="offerMessagePack" /> adds the MessagePack protocol next to JSON.</summary>
    public static async Task<TestServer> StartAsync(bool offerMessagePack)
    {
        var root = Path.Combine(Path.GetTempPath(), "g9signalr-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var wire = new WireCounter();

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0, listen => listen.Use(wire.Middleware)));
        builder.Services.AddSignalRSuperNetCoreCore();
        builder.Services.AddG9SignalRSuperNetCoreFileUpload(options =>
        {
            options.RootDirectory = root;
            options.AckEveryNChunks = 4;
        });
        if (offerMessagePack) builder.Services.AddG9SignalRSuperNetCoreMessagePack(TestShapes.GeneratedTypeShapeProvider);

        var app = builder.Build();
        app.UseWebSockets();   // the slim builder leaves it out; without it the WebSocket transport gets a 404
        app.AddSignalRSuperNetCoreServerHub<TestHub, ITestHubClient>(TestHub.Route);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new TestServer(app, wire, root, address.TrimEnd('/'));
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
public sealed class TestClient : TestHubClient
{
    private readonly TaskCompletionSource<TestReading> _poked = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TestClient(string serverUrl, TestProtocol protocol)
        : base(serverUrl, customConfigureBuilder: protocol == TestProtocol.MessagePack
            ? builder => builder.AddG9MessagePackProtocol(TestShapes.GeneratedTypeShapeProvider)
            : null)
    {
    }

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
