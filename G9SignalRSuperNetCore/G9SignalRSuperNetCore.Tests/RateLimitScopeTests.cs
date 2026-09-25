using G9SignalRSuperNetCore.Server.Classes.Errors;
using G9SignalRSuperNetCore.Server.Classes.Filters;
using G9SignalRSuperNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace G9SignalRSuperNetCore.Tests;

/// <summary>
///     2.9 rate-limit scopes. A per-connection allowance is multiplied by opening more connections; a user or IP scope
///     shares ONE bucket across them, and that bucket must still die with the last connection that shares it.
///     Every limited method here allows a burst of 3 and refills one call per 100 s.
/// </summary>
public sealed class RateLimitScopeTests
{
    [Fact]
    public async Task Connection_scope_gives_every_connection_its_own_allowance()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false);
        await using var first = await server.ConnectRawAsync(PolicyHub.Route);
        await using var second = await server.ConnectRawAsync(PolicyHub.Route);

        for (var i = 0; i < 3; i++) Assert.Equal(i, await first.InvokeAsync<int>("PerConnection", i));
        for (var i = 0; i < 3; i++) Assert.Equal(i, await second.InvokeAsync<int>("PerConnection", i));
        await AssertRateLimitedAsync(() => first.InvokeAsync<int>("PerConnection", 9));
    }

    [Fact]
    public async Task User_scope_is_shared_by_every_connection_of_the_user_and_freed_after_the_last_one()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false, userIdentifier: TestServer.UserFromQuery);
        var filter = server.Services.GetRequiredService<G9CHubFilter>();
        var alice1 = await server.ConnectRawAsync(PolicyHub.Route + "?user=alice");
        var alice2 = await server.ConnectRawAsync(PolicyHub.Route + "?user=alice");
        await using var bob = await server.ConnectRawAsync(PolicyHub.Route + "?user=bob");

        // Alice spends the whole allowance on one connection; her second connection gets nothing more.
        for (var i = 0; i < 3; i++) Assert.Equal(i, await alice1.InvokeAsync<int>("PerUser", i));
        await AssertRateLimitedAsync(() => alice2.InvokeAsync<int>("PerUser", 3));

        // Another user is not affected.
        Assert.Equal(7, await bob.InvokeAsync<int>("PerUser", 7));

        // One of Alice's connections leaving does not free the shared bucket...
        await alice1.DisposeAsync();
        await Task.Delay(200);
        await AssertRateLimitedAsync(() => alice2.InvokeAsync<int>("PerUser", 4));

        // ...the last one does: a new connection of hers starts with a full allowance.
        await alice2.DisposeAsync();
        Assert.True(await Eventually.TrueAsync(() => filter.TrackedRateLimitConnections == 1), // only Bob is left
            $"Expected only Bob's bucket, found {filter.TrackedRateLimitConnections}.");
        await using var alice3 = await server.ConnectRawAsync(PolicyHub.Route + "?user=alice");
        for (var i = 0; i < 3; i++) Assert.Equal(i, await alice3.InvokeAsync<int>("PerUser", i));

        await alice3.DisposeAsync();
        await bob.DisposeAsync();
        Assert.True(await Eventually.TrueAsync(() => filter.TrackedRateLimitConnections == 0));
    }

    [Fact]
    public async Task User_scope_without_a_user_falls_back_to_the_connection()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false, userIdentifier: TestServer.UserFromQuery);
        await using var anonymous1 = await server.ConnectRawAsync(PolicyHub.Route);
        await using var anonymous2 = await server.ConnectRawAsync(PolicyHub.Route);

        for (var i = 0; i < 3; i++) await anonymous1.InvokeAsync<int>("PerUser", i);
        for (var i = 0; i < 3; i++) Assert.Equal(i, await anonymous2.InvokeAsync<int>("PerUser", i));
    }

    [Fact]
    public async Task Ip_scope_is_shared_by_every_connection_from_the_address_and_freed_after_the_last_one()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false);
        var filter = server.Services.GetRequiredService<G9CHubFilter>();
        var first = await server.ConnectRawAsync(PolicyHub.Route);
        var second = await server.ConnectRawAsync(PolicyHub.Route);

        // Both come from 127.0.0.1.
        for (var i = 0; i < 3; i++) Assert.Equal(i, await first.InvokeAsync<int>("PerIp", i));
        await AssertRateLimitedAsync(() => second.InvokeAsync<int>("PerIp", 3));

        await first.DisposeAsync();
        await second.DisposeAsync();
        Assert.True(await Eventually.TrueAsync(() => filter.TrackedRateLimitConnections == 0),
            $"The IP bucket should die with the last connection; {filter.TrackedRateLimitConnections} owners remain.");

        await using var third = await server.ConnectRawAsync(PolicyHub.Route);
        for (var i = 0; i < 3; i++) Assert.Equal(i, await third.InvokeAsync<int>("PerIp", i));
    }

    private static async Task AssertRateLimitedAsync(Func<Task> call)
    {
        var refused = await Assert.ThrowsAsync<HubException>(call);
        Assert.Contains(G9CErrorCodes.RateLimited, refused.Message);
    }
}
