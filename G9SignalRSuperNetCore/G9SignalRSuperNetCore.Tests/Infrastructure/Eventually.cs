namespace G9SignalRSuperNetCore.Tests.Infrastructure;

/// <summary>Server-side effects of a disconnect or abort are asynchronous; poll for them with a bounded wait.</summary>
public static class Eventually
{
    public static async Task<bool> TrueAsync(Func<bool> condition, TimeSpan? within = null)
    {
        var deadline = DateTime.UtcNow + (within ?? TimeSpan.FromSeconds(10));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) return false;
            await Task.Delay(25);
        }

        return true;
    }

    /// <summary>A task that completes when <paramref name="connection"/> raises <c>Closed</c>.</summary>
    public static Task<Exception?> ClosedAsync(Microsoft.AspNetCore.SignalR.Client.HubConnection connection)
    {
        var closed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += error =>
        {
            closed.TrySetResult(error);
            return Task.CompletedTask;
        };
        return closed.Task;
    }
}
