using G9SignalRSuperNetCore.Server.Classes.Streaming;

namespace G9SignalRSuperNetCore.Tests;

/// <summary>
///     <see cref="G9CResilientStream.Run{T}"/> owns the producer for the life of the stream: a consumer that
///     walks away releases it, and a producer that throws is reported to the consumer instead of looking
///     like a stream that simply ended.
/// </summary>
public sealed class ResilientStreamTests
{
    [Fact]
    public async Task Every_item_reaches_the_consumer_and_the_stream_ends()
    {
        var items = new List<int>();
        await foreach (var item in G9CResilientStream.Run<int>(async (writer, ct) =>
                       {
                           for (var i = 0; i < 100; i++) await writer.WriteAsync(i, ct);
                       }))
        {
            items.Add(item);
        }

        Assert.Equal(Enumerable.Range(0, 100), items);
    }

    [Fact]
    public async Task A_producer_that_throws_faults_the_consumer_instead_of_ending_the_stream()
    {
        // The pair API completes the channel without the error when the producer only disposes its writer,
        // so the consumer sees a short stream and no failure. That silence is the bug worth pinning.
        var consumed = new List<int>();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var item in G9CResilientStream.Run<int>(async (writer, ct) =>
                           {
                               await writer.WriteAsync(1, ct);
                               throw new InvalidOperationException("producer gave up");
                           }))
            {
                consumed.Add(item);
            }
        });

        Assert.Equal("producer gave up", failure.Message);
        Assert.Equal([1], consumed);
    }

    [Fact]
    public async Task A_consumer_that_stops_early_releases_a_producer_blocked_on_a_full_channel()
    {
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var produced = 0;

        var stream = G9CResilientStream.Run<int>(async (writer, ct) =>
        {
            try
            {
                // Capacity is 2, so this blocks for ever unless the abandoned consumer cancels it.
                for (var i = 0; i < 1_000_000; i++)
                {
                    await writer.WriteAsync(i, ct);
                    Interlocked.Increment(ref produced);
                }
            }
            finally
            {
                released.SetResult();
            }
        }, new G9DtStreamOptions { Capacity = 2 });

        await foreach (var _ in stream) break;

        // Run awaits the producer before returning, so by here it is already over - no polling needed.
        Assert.True(released.Task.IsCompleted, "The producer must be released when the consumer stops.");
        var settled = Volatile.Read(ref produced);
        Assert.True(settled < 1000, $"The producer should have been stopped early, but wrote {settled} items.");
    }

    [Fact]
    public async Task Cancelling_the_hub_token_ends_the_stream_and_the_producer()
    {
        using var cts = new CancellationTokenSource();
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in G9CResilientStream.Run<int>(async (writer, ct) =>
                           {
                               try
                               {
                                   for (var i = 0; ; i++) await writer.WriteAsync(i, ct);
                               }
                               finally
                               {
                                   released.SetResult();
                               }
                           }, new G9DtStreamOptions { Capacity = 2 }, cts.Token))
            {
                if (item == 3) await cts.CancelAsync();
            }
        });

        Assert.True(released.Task.IsCompleted);
    }

    [Fact]
    public async Task An_empty_producer_yields_nothing_and_completes()
    {
        var any = false;
        await foreach (var _ in G9CResilientStream.Run<int>((_, _) => Task.CompletedTask)) any = true;

        Assert.False(any);
    }
}
