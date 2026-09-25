using System.Diagnostics;
using Microsoft.AspNetCore.SignalR.Client;

namespace G9SignalRSuperNetCore.Client;

/// <summary>Helpers for <see cref="HubConnection"/> (2.9).</summary>
public static class G9HubConnectionExtensions
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    ///     Completes when <paramref name="connection"/> is in the <see cref="HubConnectionState.Connected"/> state: at once
    ///     when it already is, otherwise when a reconnect completes or a state poll (every 100 ms at most) sees it
    ///     connected. Useful before sending after a network change, instead of failing the call while the automatic
    ///     reconnect is still running. It does not start the connection.
    /// </summary>
    /// <param name="connection">The connection to wait for.</param>
    /// <param name="timeout">How long to wait; <see cref="Timeout.InfiniteTimeSpan"/> waits until cancelled.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <exception cref="TimeoutException">The connection was not connected within <paramref name="timeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static async Task WaitUntilConnectedAsync(
        this HubConnection connection,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (connection is null) throw new ArgumentNullException(nameof(connection));
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(timeout), "The timeout must be non-negative or infinite.");

        if (connection.State == HubConnectionState.Connected) return;

        var reconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task OnReconnected(string? _)
        {
            reconnected.TrySetResult(true);
            return Task.CompletedTask;
        }

        connection.Reconnected += OnReconnected;
        try
        {
            var started = Stopwatch.GetTimestamp();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (connection.State == HubConnectionState.Connected) return;

                var wait = PollInterval;
                if (timeout != Timeout.InfiniteTimeSpan)
                {
                    var remaining = timeout - TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency);
                    if (remaining <= TimeSpan.Zero)
                        throw new TimeoutException($"The hub connection did not reach the Connected state within {timeout}.");
                    if (remaining < wait) wait = remaining;
                }

                var delay = Task.Delay(wait, cancellationToken);
                var signal = reconnected.Task;
                if (await Task.WhenAny(delay, signal).ConfigureAwait(false) == delay)
                    await delay.ConfigureAwait(false); // surfaces cancellation
                else
                    reconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); // re-arm
            }
        }
        finally
        {
            connection.Reconnected -= OnReconnected;
        }
    }
}
