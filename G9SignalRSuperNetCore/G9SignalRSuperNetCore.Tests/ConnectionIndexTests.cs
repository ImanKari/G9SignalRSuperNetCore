using G9SignalRSuperNetCore.Server;
using G9SignalRSuperNetCore.Server.Classes.Connections;
using G9SignalRSuperNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace G9SignalRSuperNetCore.Tests;

/// <summary>2.9 <c>IG9UserConnectionIndex</c>: who is online with how many connections, and aborting them from outside the hub.</summary>
public sealed class ConnectionIndexTests
{
    [Fact]
    public async Task The_index_counts_connections_per_user_and_aborting_a_user_closes_all_of_them()
    {
        await using var server = await StartAsync();
        var index = server.Services.GetRequiredService<IG9UserConnectionIndex>();
        var alice1 = await server.ConnectRawAsync(PolicyHub.Route + "?user=alice");
        var alice2 = await server.ConnectRawAsync(PolicyHub.Route + "?user=alice");
        await using var bob = await server.ConnectRawAsync(PolicyHub.Route + "?user=bob");
        await using var anonymous = await server.ConnectRawAsync(PolicyHub.Route);

        Assert.Equal(2, index.Count("alice"));
        Assert.Equal(new[] { alice1.ConnectionId, alice2.ConnectionId }.Order(), index.GetConnections("alice").Order());
        Assert.True(index.IsOnline("bob"));
        Assert.False(index.IsOnline("carol"));
        Assert.Equal(0, index.Count("carol"));
        Assert.Equal(new[] { "alice", "bob" }, index.OnlineUsers().Order());

        var alice1Closed = Eventually.ClosedAsync(alice1);
        var alice2Closed = Eventually.ClosedAsync(alice2);
        Assert.Equal(2, index.AbortUser("alice"));

        await Task.WhenAll(alice1Closed, alice2Closed).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HubConnectionState.Disconnected, alice1.State);
        Assert.Equal(HubConnectionState.Disconnected, alice2.State);
        Assert.True(await Eventually.TrueAsync(() => index.Count("alice") == 0));
        Assert.False(index.IsOnline("alice"));

        // Everyone else is untouched.
        Assert.Equal(HubConnectionState.Connected, bob.State);
        Assert.Equal(1, await bob.InvokeAsync<int>("Traced", 1));
        Assert.Equal(new[] { "bob" }, index.OnlineUsers());

        await alice1.DisposeAsync();
        await alice2.DisposeAsync();
    }

    [Fact]
    public async Task A_connection_without_a_user_is_reachable_only_by_its_id()
    {
        await using var server = await StartAsync();
        var index = server.Services.GetRequiredService<IG9UserConnectionIndex>();
        await using var anonymous = await server.ConnectRawAsync(PolicyHub.Route);

        Assert.Empty(index.OnlineUsers());
        Assert.False(index.AbortConnection("no-such-connection"));

        var closed = Eventually.ClosedAsync(anonymous);
        Assert.True(index.AbortConnection(anonymous.ConnectionId!));
        await closed.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(await Eventually.TrueAsync(() => !index.AbortConnection(anonymous.ConnectionId!)));
    }

    [Fact]
    public async Task Disconnecting_removes_the_connection_and_the_user_goes_offline_with_the_last_one()
    {
        await using var server = await StartAsync();
        var index = server.Services.GetRequiredService<G9CUserConnectionIndex>();
        var first = await server.ConnectRawAsync(PolicyHub.Route + "?user=dave");
        var second = await server.ConnectRawAsync(PolicyHub.Route + "?user=dave");

        await first.DisposeAsync();
        Assert.True(await Eventually.TrueAsync(() => index.Count("dave") == 1));
        Assert.True(index.IsOnline("dave"));

        await second.DisposeAsync();
        Assert.True(await Eventually.TrueAsync(() => !index.IsOnline("dave")));
        Assert.True(await Eventually.TrueAsync(() => index.ConnectionCount == 0));
    }

    [Fact]
    public void Registration_is_idempotent_and_the_interface_and_class_are_one_singleton()
    {
        var services = new ServiceCollection();
        services.AddG9SignalRSuperNetCoreConnectionIndex();
        services.AddG9SignalRSuperNetCoreConnectionIndex();
        using var provider = services.BuildServiceProvider();

        Assert.Single(services, d => d.ServiceType == typeof(IG9UserConnectionIndex));
        Assert.Same(provider.GetRequiredService<G9CUserConnectionIndex>(), provider.GetRequiredService<IG9UserConnectionIndex>());
    }

    private static Task<TestServer> StartAsync() => TestServer.StartAsync(offerMessagePack: false,
        userIdentifier: TestServer.UserFromQuery,
        configureServices: services => services.AddG9SignalRSuperNetCoreConnectionIndex());
}
