using System.Collections.Concurrent;
using System.Diagnostics;
using G9SignalRSuperNetCore.Server.Classes.Filters;
using G9SignalRSuperNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.SignalR.Client;

namespace G9SignalRSuperNetCore.Tests;

/// <summary>
///     2.9: whether a method streams is read from its DECLARED return type (no reflection over the runtime type, which
///     raised IL2070), and a telemetry span can be sampled.
/// </summary>
public sealed class StreamingTelemetryTests : IDisposable
{
    private readonly ConcurrentQueue<Activity> _stopped = new();
    private readonly ActivityListener _listener;

    public StreamingTelemetryTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == G9CTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _stopped.Enqueue(activity)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    [Theory]
    [InlineData("Count", "PolicyHub.Count")]      // IAsyncEnumerable<int>
    [InlineData("Channel", "PolicyHub.Channel")]  // ChannelReader<int>
    public async Task A_streaming_method_with_telemetry_still_streams_and_is_tagged_as_a_stream(string method, string span)
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false);
        await using var connection = await server.ConnectRawAsync(PolicyHub.Route);

        var items = new List<int>();
        await foreach (var item in connection.StreamAsync<int>(method, 5)) items.Add(item);

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, items);
        Assert.True(await Eventually.TrueAsync(() => Find(span, connection.ConnectionId) is not null), "No span was recorded.");
        var activity = Find(span, connection.ConnectionId)!;
        Assert.True(activity.GetTagItem("g9.stream") is true, "The span should be tagged g9.stream=true.");
        Assert.Equal("stream_started", activity.GetTagItem("g9.outcome"));
    }

    [Fact]
    public async Task A_plain_method_is_not_tagged_as_a_stream()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false);
        await using var connection = await server.ConnectRawAsync(PolicyHub.Route);

        Assert.Equal(4, await connection.InvokeAsync<int>("Traced", 4));

        Assert.True(await Eventually.TrueAsync(() => Find("PolicyHub.Traced", connection.ConnectionId) is not null));
        var activity = Find("PolicyHub.Traced", connection.ConnectionId)!;
        Assert.Null(activity.GetTagItem("g9.stream"));
        Assert.Equal("ok", activity.GetTagItem("g9.outcome"));
    }

    [Fact]
    public async Task A_sample_rate_of_zero_records_no_span_but_still_runs_the_method()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false);
        await using var connection = await server.ConnectRawAsync(PolicyHub.Route);

        for (var i = 0; i < 20; i++) Assert.Equal(i, await connection.InvokeAsync<int>("Unsampled", i));
        Assert.Equal(1, await connection.InvokeAsync<int>("Traced", 1)); // proves the listener is live

        Assert.True(await Eventually.TrueAsync(() => Find("PolicyHub.Traced", connection.ConnectionId) is not null));
        Assert.Null(Find("PolicyHub.Unsampled", connection.ConnectionId));
    }

    private Activity? Find(string name, string? connectionId) =>
        _stopped.FirstOrDefault(a => a.OperationName == name && Equals(a.GetTagItem("g9.connection_id"), connectionId));

    public void Dispose() => _listener.Dispose();
}
