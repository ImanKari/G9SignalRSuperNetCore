using G9SignalRSuperNetCore.Client;
using G9SignalRSuperNetCore.Server;
using G9SignalRSuperNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace G9SignalRSuperNetCore.Tests;

/// <summary>
///     SignalR's stateful reconnect as an opt-in on both sides (2.8.0): the client asks through
///     <see cref="G9DtClientConnectionOptions.UseStatefulReconnect" />, the endpoint allows it through the mapping
///     helper's <c>allowStatefulReconnects</c>, and a connection cut underneath them is resumed instead of replaced.
/// </summary>
public sealed class StatefulReconnectTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(TestProtocol.Json)]
    [InlineData(TestProtocol.MessagePack)]
    public async Task A_client_that_asks_gets_it_on_an_endpoint_that_allows_it_and_everything_still_works(TestProtocol protocol)
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: true);
        await using var client = new StatefulTestClient(server.BaseUrl + TestServer.StatefulPrefix, protocol);

        await client.ConnectAsync();

        Assert.Equal("on", await client.Server.StatefulReconnect());
        Assert.Equal(protocol == TestProtocol.MessagePack ? "Binary" : "Text", await client.Server.TransferFormat());
        var sent = TestReading.Create(12);
        Assert.Equal(sent.Describe(), (await client.Server.Echo(sent)).Describe());
        var streamed = new List<string>();
        await foreach (var reading in client.Server.Readings(50, CancellationToken.None)) streamed.Add(reading.Describe());
        Assert.Equal(Enumerable.Range(1, 50).Select(i => TestReading.Create(i).Describe()), streamed);
        Assert.Equal(5050, await client.Server.Sum(Count(1, 100)));
        await client.Server.Poke(3);
        Assert.Equal(TestReading.Create(3).Describe(), (await client.FirstPoke.WaitAsync(TimeSpan.FromSeconds(10))).Describe());
    }

    [Fact]
    public async Task It_stays_off_unless_both_sides_opted_in()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false);

        await using var didNotAsk = new TestClient(server.BaseUrl + TestServer.StatefulPrefix, TestProtocol.Json);
        await didNotAsk.ConnectAsync();
        Assert.Equal("off", await didNotAsk.Server.StatefulReconnect());

        await using var notAllowed = new StatefulTestClient(server.BaseUrl, TestProtocol.Json);
        await notAllowed.ConnectAsync();
        Assert.Equal("off", await notAllowed.Server.StatefulReconnect());
        Assert.Equal(7, await notAllowed.Server.Limited(7));
    }

    [Theory]
    [InlineData(TestProtocol.Json)]
    [InlineData(TestProtocol.MessagePack)]
    public async Task A_cut_connection_is_resumed_under_the_same_id_with_the_option_and_replaced_without_it(TestProtocol protocol)
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: true);

        await using (var stateful = new StatefulTestClient(server.BaseUrl + TestServer.StatefulPrefix, protocol))
        {
            await stateful.ConnectAsync();
            var idBefore = await stateful.Server.ServerConnectionId();

            Assert.True(server.Probe.CutConnections() > 0);

            // Issued into the break: it is buffered, and answered once the same connection is back.
            Assert.Equal(21, await stateful.Server.Limited(21).WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.Equal(idBefore, await stateful.Server.ServerConnectionId());
            Assert.Equal(idBefore, stateful.Connection.ConnectionId);
            // The HubConnection never noticed: no Reconnecting, no Reconnected, no Disconnected. Neither did the hub.
            Assert.Equal([G9EConnectionPhase.Connecting, G9EConnectionPhase.Connected], stateful.Phases.Select(p => p.Phase));
            Assert.False(TestHub.Disconnected.ContainsKey(idBefore), "The hub was told the resumed connection had ended.");
        }

        await using var ordinary = new TestClient(server.BaseUrl + TestServer.StatefulPrefix, protocol);
        await ordinary.ConnectAsync();
        var ordinaryIdBefore = await ordinary.Server.ServerConnectionId();

        Assert.True(server.Probe.CutConnections() > 0);

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (ordinary.Phases.All(p => p.Phase != G9EConnectionPhase.Reconnected) && DateTime.UtcNow < deadline) await Task.Delay(50);
        output.WriteLine(protocol + ": " + string.Join(", ", ordinary.Phases.Select(p => p.Phase)));
        Assert.Contains(ordinary.Phases, p => p.Phase == G9EConnectionPhase.Reconnecting);
        Assert.Contains(ordinary.Phases, p => p.Phase == G9EConnectionPhase.Reconnected);
        Assert.NotEqual(ordinaryIdBefore, await ordinary.Server.ServerConnectionId());
        while (!TestHub.Disconnected.ContainsKey(ordinaryIdBefore) && DateTime.UtcNow < deadline) await Task.Delay(50);
        Assert.True(TestHub.Disconnected.ContainsKey(ordinaryIdBefore), "The hub was not told the replaced connection had ended.");
    }

    [Fact]
    public async Task With_WebSockets_first_also_on_stateful_reconnect_wins_and_the_client_negotiates()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false);
        await using var client = new StatefulWebSocketsFirstTestClient(server.BaseUrl + TestServer.StatefulPrefix, TestProtocol.Json);

        await client.ConnectAsync();

        Assert.Equal(1, server.Probe.Count("POST", TestServer.StatefulPrefix + TestHub.Route + "/negotiate"));
        Assert.Equal("on", await client.Server.StatefulReconnect());
        Assert.Equal("WebSockets", await client.Server.Transport());
    }

    [Fact]
    public async Task Why_it_wins_a_connection_that_skips_negotiation_does_not_get_stateful_reconnect()
    {
        // SignalR agrees on stateful reconnect in the negotiate response. If a later SignalR carries it without negotiation,
        // this fails and the two options can be combined.
        await using var server = await TestServer.StartAsync(offerMessagePack: false);
        await using var raw = new HubConnectionBuilder()
            .WithUrl(server.BaseUrl + TestServer.StatefulPrefix + TestHub.Route, options =>
            {
                options.SkipNegotiation = true;
                options.Transports = HttpTransportType.WebSockets;
            })
            .WithStatefulReconnect()
            .Build();

        await raw.StartAsync();

        Assert.Equal("off", await raw.InvokeAsync<string>(nameof(TestHub.StatefulReconnect)));
    }

    [Fact]
    public async Task The_buffer_sizes_reach_the_options_SignalR_reads()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false, statefulReconnectBufferSize: 250_000);
        // A hub without options of its own reads the global HubOptions...
        Assert.Equal(250_000, server.Services.GetRequiredService<IOptions<HubOptions>>().Value.StatefulReconnectBufferSize);

        // ...and a hub WITH its own (AddG9SignalRSuperNetCoreGroups adds per-hub filters) reads those. SignalR seeds them from the
        // global ones but leaves this member out, so without the library's help the hub would stay on the default 100,000.
        var withPerHubOptions = new ServiceCollection().AddLogging();
        withPerHubOptions.AddSignalRSuperNetCoreCore();
        withPerHubOptions.AddG9SignalRSuperNetCoreGroups<TestHub>();
        withPerHubOptions.AddG9SignalRSuperNetCoreStatefulReconnect(250_000);
        using (var provider = withPerHubOptions.BuildServiceProvider())
        {
            Assert.Equal(250_000, provider.GetRequiredService<IOptions<HubOptions<TestHub>>>().Value.StatefulReconnectBufferSize);
            Assert.Equal(250_000, provider.GetRequiredService<IOptions<HubOptions>>().Value.StatefulReconnectBufferSize);
        }

        // A hub that was given a size of its own keeps it.
        var withItsOwnSize = new ServiceCollection().AddLogging();
        withItsOwnSize.AddSignalRSuperNetCoreCore();
        withItsOwnSize.AddG9SignalRSuperNetCoreStatefulReconnect(250_000);
        withItsOwnSize.AddSignalR().AddHubOptions<TestHub>(options => options.StatefulReconnectBufferSize = 32_000);
        using (var provider = withItsOwnSize.BuildServiceProvider())
            Assert.Equal(32_000, provider.GetRequiredService<IOptions<HubOptions<TestHub>>>().Value.StatefulReconnectBufferSize);

        await using var client = new BufferSizedTestClient(server.BaseUrl + TestServer.StatefulPrefix);
        using var clientServices = client.BuilderServices!.BuildServiceProvider();
        Assert.Equal(64_000, clientServices.GetRequiredService<IOptions<HubConnectionOptions>>().Value.StatefulReconnectBufferSize);
        await client.ConnectAsync();
        Assert.Equal("on", await client.Server.StatefulReconnect());

        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection().AddG9SignalRSuperNetCoreStatefulReconnect(0));
    }

    private static async IAsyncEnumerable<int> Count(int from, int to)
    {
        for (var i = from; i <= to; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }

    /// <summary>Sets the client buffer, and keeps the builder's service collection so the test can read what was configured.</summary>
    private sealed class BufferSizedTestClient : TestHubClient
    {
        [ThreadStatic] private static IServiceCollection? _captured;

        public BufferSizedTestClient(string serverUrl) : base(serverUrl, customConfigureBuilder: builder =>
        {
            _captured = builder.Services;
            return builder;
        })
        {
            BuilderServices = _captured;
        }

        public IServiceCollection? BuilderServices { get; }

        protected override void ConfigureConnectionOptions(G9DtClientConnectionOptions options)
        {
            options.UseStatefulReconnect = true;
            options.StatefulReconnectBufferSize = 64_000;
        }
    }
}
