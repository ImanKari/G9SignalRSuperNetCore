using G9SignalRSuperNetCore.Client;
using Microsoft.AspNetCore.SignalR.Client;

namespace G9SignalRSuperNetCore.Tests;

/// <summary>
///     One <see cref="G9CClientReconnectPolicy" /> may serve several connections, and each asks for its next delay on
///     its own reconnect loop. Up to 2.7.0 the policy drew its jitter from one captured <see cref="Random" />, which
///     is not thread-safe: racing callers are handed the same samples, and the older generator can corrupt its state
///     and return 0 for ever. A jitter sample that is always 0 is no jitter — every client retries at exactly 85% of
///     the nominal delay, in step. (This project runs the net10.0 build, i.e. <c>Random.Shared</c>; the
///     netstandard2.1 build's locked <see cref="Random" /> is not exercised here.)
/// </summary>
public sealed class ReconnectPolicyJitterTests(ITestOutputHelper output)
{
    [Fact]
    public void Delays_asked_for_from_many_threads_stay_inside_the_jitter_band_and_keep_varying()
    {
        // factor 1 and maxDelay == baseDelay: the nominal delay is 1000 ms whatever the retry count, so the band is fixed.
        const double Nominal = 1000, Low = Nominal * 0.85, High = Nominal * 1.15;
        var policy = new G9CClientReconnectPolicy(TimeSpan.FromMilliseconds(Nominal), 1.0, TimeSpan.FromMilliseconds(Nominal), Timeout.InfiniteTimeSpan);
        var threads = Math.Max(8, Environment.ProcessorCount * 2);
        const int PerThread = 200_000;

        long outside = 0, atTheFloor = 0;
        var distinctInTheLastStretch = new int[threads];
        using var start = new Barrier(threads);
        var workers = Enumerable.Range(0, threads).Select(t => new Thread(() =>
        {
            var context = new RetryContext { PreviousRetryCount = t, ElapsedTime = TimeSpan.Zero };
            var tail = new HashSet<double>();
            start.SignalAndWait();
            for (var i = 0; i < PerThread; i++)
            {
                var delay = policy.NextRetryDelay(context)!.Value.TotalMilliseconds;
                if (delay < Low - 0.001 || delay > High + 0.001) Interlocked.Increment(ref outside);
                // A generator that broke returns 0, which lands exactly on the floor of the band.
                if (Math.Abs(delay - Low) < 0.0001) Interlocked.Increment(ref atTheFloor);
                // What matters is that it STILL varies after all the contention, so look at the end of the run.
                if (i >= PerThread - 1_000) tail.Add(delay);
            }

            distinctInTheLastStretch[t] = tail.Count;
        })).ToList();

        workers.ForEach(w => w.Start());
        workers.ForEach(w => w.Join());

        output.WriteLine($"{threads} threads x {PerThread:N0} delays: {outside} outside the band, {atTheFloor} on its floor, " +
                         $"fewest distinct values in a thread's last 1,000: {distinctInTheLastStretch.Min()}");
        Assert.Equal(0, Interlocked.Read(ref outside));
        // A TimeSpan counts 100 ns ticks and the band is 3,000,000 of them wide, so a healthy generator lands on the floor about
        // once in three million draws; a broken one does every time. The bar is thirty times the healthy rate.
        Assert.True(Interlocked.Read(ref atTheFloor) * 100_000 < (long)threads * PerThread, $"{atTheFloor} delays had no jitter at all.");
        // 1,000 draws from 3,000,000 values collide about once in six runs. Threads racing on one unguarded Random hand each
        // other the same samples: against the 2.7.0 policy this measured between 640 and 920 distinct values.
        Assert.All(distinctInTheLastStretch, distinct => Assert.True(distinct >= 990, $"Only {distinct} distinct delays in a thread's last 1,000."));
    }

    [Fact]
    public void The_delay_still_grows_is_capped_and_stops_when_the_budget_is_spent()
    {
        var policy = new G9CClientReconnectPolicy(TimeSpan.FromMilliseconds(200), 2.0, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5));

        var third = policy.NextRetryDelay(new RetryContext { PreviousRetryCount = 3 })!.Value.TotalMilliseconds;   // nominal 1600
        Assert.InRange(third, 1600 * 0.85, 1600 * 1.15);

        var capped = policy.NextRetryDelay(new RetryContext { PreviousRetryCount = 20 })!.Value.TotalMilliseconds; // nominal 30 s
        Assert.InRange(capped, 30_000 * 0.85, 30_000 * 1.15);

        Assert.Null(policy.NextRetryDelay(new RetryContext { PreviousRetryCount = 1, ElapsedTime = TimeSpan.FromMinutes(5) }));
    }
}
