using G9SignalRSuperNetCore.Server.Classes.Filters;
using G9SignalRSuperNetCore.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace G9SignalRSuperNetCore.Tests;

/// <summary>
///     Rate-limit buckets belong to a connection and must die with it. The filter is a singleton, so a
///     bucket that outlives its connection is never reachable again and never freed - on mobile, where a
///     tunnel change means a new connection, that is a slow leak for the life of the process.
/// </summary>
public sealed class RateLimitLifetimeTests
{
    [Fact]
    public async Task Connections_that_used_a_rate_limited_method_release_their_buckets()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false);
        var filter = server.Services.GetRequiredService<G9CHubFilter>();

        for (var i = 0; i < 25; i++)
        {
            await using var client = await server.ConnectAsync(TestProtocol.Json);
            Assert.Equal(i, await client.Server.Limited(i));
            Assert.True(filter.TrackedRateLimitConnections >= 1, "The live connection should hold a bucket.");
        }

        // Each client is disposed at the end of its iteration, so nothing should be left holding buckets.
        await WaitForAsync(() => filter.TrackedRateLimitConnections == 0);
        Assert.Equal(0, filter.TrackedRateLimitConnections);
    }

    [Fact]
    public async Task A_connection_that_never_called_a_limited_method_holds_nothing()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false);
        var filter = server.Services.GetRequiredService<G9CHubFilter>();

        await using (var client = await server.ConnectAsync(TestProtocol.Json))
        {
            await client.Server.Echo(TestReading.Create(1));
            Assert.Equal(0, filter.TrackedRateLimitConnections);
        }

        Assert.Equal(0, filter.TrackedRateLimitConnections);
    }

    /// <summary>Disconnect is observed by the server asynchronously, so give it a bounded moment to run.</summary>
    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(50);
    }
}
