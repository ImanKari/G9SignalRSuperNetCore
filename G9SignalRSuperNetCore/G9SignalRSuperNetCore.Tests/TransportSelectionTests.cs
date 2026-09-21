using G9SignalRSuperNetCore.Client;
using G9SignalRSuperNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.SignalR.Client;

namespace G9SignalRSuperNetCore.Tests;

/// <summary>
///     <see cref="G9DtClientConnectionOptions.WebSocketsFirst" /> (2.8.0): connect over WebSockets without the negotiate
///     request, and when that fails for a reason negotiation can cure, try once more with negotiation — on the SAME
///     <see cref="HubConnection" />, so nothing registered on it is lost.
/// </summary>
public sealed class TransportSelectionTests(ITestOutputHelper output)
{
    private const string Negotiate = TestHub.Route + "/negotiate";

    [Theory]
    [InlineData(TestProtocol.Json)]
    [InlineData(TestProtocol.MessagePack)]
    public async Task A_client_that_turns_nothing_on_negotiates_exactly_as_before(TestProtocol protocol)
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: true);
        await using var client = await server.ConnectAsync(protocol);

        Assert.Equal("WebSockets", await client.Server.Transport());
        Assert.Equal("off", await client.Server.StatefulReconnect());
        Assert.Equal(1, server.Probe.Count("POST", Negotiate));
        Assert.Equal([G9EConnectionPhase.Connecting, G9EConnectionPhase.Connected], client.Phases.Select(p => p.Phase));
    }

    [Theory]
    [InlineData(TestProtocol.Json)]
    [InlineData(TestProtocol.MessagePack)]
    public async Task WebSockets_first_connects_without_a_negotiate_request(TestProtocol protocol)
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: true);
        await using var client = new WebSocketsFirstTestClient(server.BaseUrl, protocol);

        await client.ConnectAsync();

        Assert.Equal(0, server.Probe.Count("POST", Negotiate));
        Assert.Equal("WebSockets", await client.Server.Transport());
        await AssertInvokesAndStreams(client);
        Assert.Equal([G9EConnectionPhase.Connecting, G9EConnectionPhase.Connected], client.Phases.Select(p => p.Phase));
    }

    [Theory]
    [InlineData(TestProtocol.Json)]
    [InlineData(TestProtocol.MessagePack)]
    public async Task WebSockets_first_falls_back_to_negotiation_when_the_endpoint_refuses_WebSockets(TestProtocol protocol)
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: true);
        await using var client = new WebSocketsFirstTestClient(server.BaseUrl + TestServer.NoWebSocketsPrefix, protocol);

        // What an app does between constructing the client and connecting it: keep the connection, hang a handler on it.
        var connectionBefore = client.Connection;
        var pokedOnRawHandler = new TaskCompletionSource<TestReading>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var rawHandler = client.Connection.On<TestReading>("Poked", reading => pokedOnRawHandler.TrySetResult(reading));

        await client.ConnectAsync();

        var transport = await client.Server.Transport();
        output.WriteLine($"{protocol}: ended up on {transport}; fallback detail: {client.Phases.Single(p => p.Phase == G9EConnectionPhase.TransportFallback).Detail}");
        Assert.Contains(transport, new[] { "ServerSentEvents", "LongPolling" });
        Assert.Equal(1, server.Probe.Count("POST", TestServer.NoWebSocketsPrefix + Negotiate));
        Assert.Equal([G9EConnectionPhase.Connecting, G9EConnectionPhase.TransportFallback, G9EConnectionPhase.Connected], client.Phases.Select(p => p.Phase));

        // The fallback did not rebuild anything: same HubConnection, and the handler registered on it before the connect still fires.
        Assert.Same(connectionBefore, client.Connection);
        await AssertInvokesAndStreams(client);
        await client.Server.Poke(4);
        Assert.Equal(TestReading.Create(4).Describe(), (await pokedOnRawHandler.Task.WaitAsync(TimeSpan.FromSeconds(10))).Describe());
        Assert.Equal(TestReading.Create(4).Describe(), (await client.FirstPoke.WaitAsync(TimeSpan.FromSeconds(10))).Describe());
    }

    [Fact]
    public async Task Every_ConnectAsync_starts_with_WebSockets_again()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false);
        await using var client = new WebSocketsFirstTestClient(server.BaseUrl + TestServer.NoWebSocketsPrefix, TestProtocol.Json);

        await client.ConnectAsync();
        await client.DisconnectAsync();
        await client.ConnectAsync();

        // One direct WebSocket attempt (a GET on the hub route with no connection id) and one negotiate per ConnectAsync.
        Assert.Equal(2, client.Phases.Count(p => p.Phase == G9EConnectionPhase.TransportFallback));
        Assert.Equal(2, server.Probe.Count("POST", TestServer.NoWebSocketsPrefix + Negotiate));
        Assert.Equal(1, await client.Server.Limited(1));
    }

    [Theory]
    [InlineData(TestServer.UnauthorizedPrefix)]
    [InlineData(TestServer.ForbiddenPrefix)]
    public async Task An_authentication_failure_is_not_answered_with_a_second_attempt(string prefix)
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false);
        await using var client = new WebSocketsFirstTestClient(server.BaseUrl + prefix, TestProtocol.Json);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync());

        output.WriteLine(Describe(error));
        Assert.Equal(["GET " + prefix + TestHub.Route], server.Probe.Requests);
        Assert.Equal([G9EConnectionPhase.Connecting, G9EConnectionPhase.ConnectFailed], client.Phases.Select(p => p.Phase));
    }

    [Fact]
    public async Task A_refusal_by_the_hub_itself_is_not_answered_with_a_second_attempt()
    {
        // The WebSocket connected and the hub answered the handshake with an error: the transport is fine, negotiating cures nothing.
        await using var server = await TestServer.StartAsync(offerMessagePack: false);
        await using var client = new WebSocketsFirstTestClient(server.BaseUrl, TestProtocol.MessagePack);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync());

        output.WriteLine(Describe(error));
        Assert.Contains("messagepack", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, server.Probe.Count("POST", Negotiate));
        Assert.DoesNotContain(client.Phases, p => p.Phase == G9EConnectionPhase.TransportFallback);
    }

    [Fact]
    public async Task When_the_second_attempt_fails_too_the_caller_gets_the_failure_of_the_negotiation()
    {
        string deadUrl;
        await using (var gone = await TestServer.StartAsync(offerMessagePack: false)) deadUrl = gone.BaseUrl;
        await using var client = new WebSocketsFirstTestClient(deadUrl, TestProtocol.Json);
        await using var plain = new TestClient(deadUrl, TestProtocol.Json);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync());
        var withoutTheOption = await Assert.ThrowsAnyAsync<Exception>(() => plain.ConnectAsync());

        output.WriteLine(Describe(error));
        Assert.Equal(withoutTheOption.GetType(), error.GetType());
        Assert.Equal([G9EConnectionPhase.Connecting, G9EConnectionPhase.TransportFallback, G9EConnectionPhase.ConnectFailed], client.Phases.Select(p => p.Phase));
        Assert.Equal(HubConnectionState.Disconnected, client.Connection.State);
    }

    [Fact]
    public async Task A_cancelled_connect_is_not_answered_with_a_second_attempt()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false);
        await using var client = new WebSocketsFirstTestClient(server.BaseUrl + TestServer.NoWebSocketsPrefix, TestProtocol.Json);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ConnectAsync(cancelled.Token));

        Assert.Empty(server.Probe.Requests);
        Assert.DoesNotContain(client.Phases, p => p.Phase == G9EConnectionPhase.TransportFallback);
    }

    private static async Task AssertInvokesAndStreams(TestClient client)
    {
        var sent = TestReading.Create(8);
        Assert.Equal(sent.Describe(), (await client.Server.Echo(sent)).Describe());

        var streamed = new List<string>();
        await foreach (var reading in client.Server.Readings(5, CancellationToken.None)) streamed.Add(reading.Describe());
        Assert.Equal(Enumerable.Range(1, 5).Select(i => TestReading.Create(i).Describe()), streamed);

        Assert.Equal(5050, await client.Server.Sum(Count(1, 100)));
    }

    private static async IAsyncEnumerable<int> Count(int from, int to)
    {
        for (var i = from; i <= to; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }

    private static string Describe(Exception error)
    {
        var parts = new List<string>();
        for (var e = error; e is not null; e = e.InnerException) parts.Add(e.GetType().Name + ": " + e.Message);
        return string.Join(" <- ", parts);
    }
}
