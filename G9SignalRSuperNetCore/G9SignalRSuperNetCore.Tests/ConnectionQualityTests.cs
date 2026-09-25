using System.Collections.Concurrent;
using System.Diagnostics;
using G9SignalRSuperNetCore.Client;
using G9SignalRSuperNetCore.Server.Classes.Errors;
using G9SignalRSuperNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace G9SignalRSuperNetCore.Tests;

/// <summary>2.9: the built-in <c>G9Ping</c> hub method, <c>G9CConnectionQualityMonitor</c>, and <c>WaitUntilConnectedAsync</c>.</summary>
public sealed class ConnectionQualityTests
{
    [Fact]
    public async Task G9Ping_echoes_the_timestamp_on_every_G9_hub()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false);
        await using var test = await server.ConnectRawAsync(TestHub.Route);
        await using var policy = await server.ConnectRawAsync(PolicyHub.Route);

        Assert.Equal(42L, await test.InvokeAsync<long>("G9Ping", 42L));
        Assert.Equal(long.MaxValue, await policy.InvokeAsync<long>("G9Ping", long.MaxValue));
    }

    [Fact]
    public async Task G9Ping_is_rate_limited_per_connection()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false);
        await using var connection = await server.ConnectRawAsync(TestHub.Route);

        // Burst 5, refill 2/s: ten back-to-back pings cannot all pass.
        var refused = 0;
        for (var i = 0; i < 10; i++)
        {
            try
            {
                await connection.InvokeAsync<long>("G9Ping", i);
            }
            catch (HubException ex) when (ex.Message.Contains(G9CErrorCodes.RateLimited))
            {
                refused++;
            }
        }

        Assert.True(refused > 0, "Ten immediate pings should exceed the burst of five.");
    }

    [Fact]
    public async Task G9Ping_is_not_emitted_into_the_generated_typed_client()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false);
        await using var client = await server.ConnectAsync(TestProtocol.Json);

        Assert.DoesNotContain(client.Server.GetType().GetMethods(), m => m.Name == "G9Ping");
        Assert.DoesNotContain(typeof(TestClient).GetMethods(), m => m.Name == "G9Ping");
    }

    [Fact]
    public async Task The_monitor_reports_Good_on_loopback_and_Lost_once_the_server_is_gone()
    {
        var server = await TestServer.StartAsync(offerMessagePack: false);
        var connection = await server.ConnectRawAsync(TestHub.Route);
        var levels = new ConcurrentQueue<G9EConnectionQualityLevel>();
        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await using var monitor = new G9CConnectionQualityMonitor(connection, TimeSpan.FromMilliseconds(500));
            monitor.QualityChanged += quality =>
            {
                levels.Enqueue(quality.Level);
                if (quality.Level == G9EConnectionQualityLevel.Lost) lost.TrySetResult();
            };
            monitor.Start();

            // The first probe of a cold server may be slow; loopback settles well under 150 ms within a few probes.
            G9DtConnectionQuality measured = default;
            Assert.True(await Eventually.TrueAsync(
                    () => (measured = monitor.Current) is { RttMs: >= 0, Level: G9EConnectionQualityLevel.Good },
                    TimeSpan.FromSeconds(15)),
                $"Expected a Good measurement on loopback, last was {monitor.Current}.");
            Assert.True(measured.RttMs < G9CConnectionQualityMonitor.GoodBelowMs);
            Assert.NotEqual(default, measured.MeasuredUtc);

            await server.DisposeAsync();

            await lost.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(G9EConnectionQualityLevel.Lost, monitor.Current.Level);
            Assert.Equal(-1, monitor.Current.RttMs);
            Assert.Equal(G9EConnectionQualityLevel.Lost, levels.Last());
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task The_monitor_rejects_bad_arguments_and_cannot_start_after_disposal()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false);
        await using var connection = await server.ConnectRawAsync(TestHub.Route);

        Assert.Throws<ArgumentOutOfRangeException>(() => new G9CConnectionQualityMonitor(connection, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => new G9CConnectionQualityMonitor(connection, pingMethod: ""));

        var monitor = new G9CConnectionQualityMonitor(connection);
        Assert.Equal(G9EConnectionQualityLevel.Good, monitor.Current.Level); // connected, not yet measured
        Assert.Equal(-1, monitor.Current.RttMs);
        await monitor.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(monitor.Start);
    }

    [Fact]
    public async Task WaitUntilConnectedAsync_returns_at_once_for_a_connected_connection()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false);
        await using var connection = await server.ConnectRawAsync(TestHub.Route);

        var clock = Stopwatch.StartNew();
        await connection.WaitUntilConnectedAsync(TimeSpan.FromSeconds(5));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task WaitUntilConnectedAsync_times_out_for_a_connection_that_never_connects()
    {
        await using var connection = new HubConnectionBuilder().WithUrl("http://127.0.0.1:9/never").Build();

        var clock = Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(() => connection.WaitUntilConnectedAsync(TimeSpan.FromMilliseconds(300)));
        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(250), $"Gave up after only {clock.Elapsed}.");
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WaitUntilConnectedAsync_honours_cancellation()
    {
        await using var connection = new HubConnectionBuilder().WithUrl("http://127.0.0.1:9/never").Build();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => connection.WaitUntilConnectedAsync(Timeout.InfiniteTimeSpan, cancel.Token));
    }
}
