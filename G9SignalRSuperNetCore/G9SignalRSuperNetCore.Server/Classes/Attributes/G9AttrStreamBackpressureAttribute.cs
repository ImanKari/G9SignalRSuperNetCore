using G9SignalRSuperNetCore.Server.Classes.Streaming;

namespace G9SignalRSuperNetCore.Server.Classes.Attributes;

/// <summary>
///     Declarative backpressure metadata for a server-streaming hub method. Hub authors can
///     annotate a method with this attribute and read it back through reflection or use it as
///     a guideline when constructing a <see cref="G9CResilientStream"/> producer/consumer pair.
/// </summary>
/// <remarks>
///     <para>The attribute is a static contract — there is no runtime hook that automatically
///     wraps the method's return value. Hub authors who want backpressure must call
///     <see cref="G9CResilientStream.Create{T}(G9DtStreamOptions?)"/> from inside the method.
///     Source-generator-driven defaults can read the attribute at build time to scaffold the
///     wrapper for them.</para>
///     <para>Zero-cost: no runtime impact when the attribute is absent.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class G9AttrStreamBackpressureAttribute : Attribute
{
    /// <summary>Bounded buffer capacity. Must be positive.</summary>
    public int Capacity { get; }

    /// <summary>Behavior when the buffer is full.</summary>
    public G9EStreamDropPolicy DropPolicy { get; }

    /// <summary>Initializes the attribute.</summary>
    /// <param name="capacity">Bounded buffer capacity (default 256).</param>
    /// <param name="dropPolicy">Behavior on overflow (default <see cref="G9EStreamDropPolicy.Wait"/>).</param>
    public G9AttrStreamBackpressureAttribute(int capacity = 256, G9EStreamDropPolicy dropPolicy = G9EStreamDropPolicy.Wait)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
        DropPolicy = dropPolicy;
    }

    /// <summary>Materializes a <see cref="G9DtStreamOptions"/> instance from the attribute.</summary>
    public G9DtStreamOptions ToOptions() => new() { Capacity = Capacity, DropPolicy = DropPolicy };
}
