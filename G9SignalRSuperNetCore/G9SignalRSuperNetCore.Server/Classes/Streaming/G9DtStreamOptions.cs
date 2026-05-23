namespace G9SignalRSuperNetCore.Server.Classes.Streaming;

/// <summary>Strategy for handling overflow on a bounded stream channel.</summary>
public enum G9EStreamDropPolicy
{
    /// <summary>Block the producer until the consumer drains an item (default).</summary>
    Wait,
    /// <summary>Drop the new item silently, preserving older items.</summary>
    DropNewest,
    /// <summary>Drop the oldest queued item to make room for the new one.</summary>
    DropOldest
}

/// <summary>Options controlling backpressure on G9 server-to-client streams.</summary>
public sealed class G9DtStreamOptions
{
    /// <summary>Bounded buffer capacity. Default 256 items.</summary>
    public int Capacity { get; set; } = 256;

    /// <summary>Behavior when the buffer is full. Default <see cref="G9EStreamDropPolicy.Wait"/>.</summary>
    public G9EStreamDropPolicy DropPolicy { get; set; } = G9EStreamDropPolicy.Wait;
}
