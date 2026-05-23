namespace G9SignalRSuperNetCore.Server.Classes.Distributed;

/// <summary>
///     A small abstraction for cross-node coordination. Implementations let the G9 group manager
///     and presence tracker share state across multiple server processes (e.g. several Kestrel
///     instances behind a load balancer).
/// </summary>
/// <remarks>
///     <para>The default registration is <see cref="G9CInProcessBackplane"/>, which is a no-op
///     suitable for single-process deployments. Replace it with a Redis or NATS implementation
///     when scaling out; SignalR's own Redis backplane handles message fan-out, while this
///     interface handles the G9-specific membership state.</para>
///     <para>The contract is intentionally minimal: implementations are responsible for atomic
///     publish/subscribe and best-effort delivery; consumers must tolerate occasional duplicates
///     or missed events.</para>
/// </remarks>
public interface IG9DistributedBackplane : IAsyncDisposable
{
    /// <summary>
    ///     Publishes a payload to <paramref name="topic"/>. Returns when the message has been
    ///     handed to the transport (not when subscribers have acknowledged it).
    /// </summary>
    ValueTask PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken ct = default);

    /// <summary>
    ///     Subscribes to <paramref name="topic"/>. The returned token is disposable; disposing
    ///     unsubscribes. The handler runs on a backplane-owned thread; offload heavy work.
    /// </summary>
    ValueTask<IAsyncDisposable> SubscribeAsync(string topic, Func<ReadOnlyMemory<byte>, ValueTask> handler, CancellationToken ct = default);
}

/// <summary>
///     No-op backplane that satisfies the contract for single-process deployments. Publishes
///     are dropped because there are no other nodes to receive them; subscribers receive
///     nothing.
/// </summary>
public sealed class G9CInProcessBackplane : IG9DistributedBackplane
{
    /// <inheritdoc />
    public ValueTask PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken ct = default) => default;

    /// <inheritdoc />
    public ValueTask<IAsyncDisposable> SubscribeAsync(string topic, Func<ReadOnlyMemory<byte>, ValueTask> handler, CancellationToken ct = default)
        => ValueTask.FromResult<IAsyncDisposable>(NoopDisposable.Instance);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => default;

    private sealed class NoopDisposable : IAsyncDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public ValueTask DisposeAsync() => default;
    }
}
