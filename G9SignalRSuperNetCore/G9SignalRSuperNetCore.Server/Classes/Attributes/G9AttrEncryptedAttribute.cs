namespace G9SignalRSuperNetCore.Server.Classes.Attributes;

/// <summary>
///     Marks a hub method so that the consumer is expected to wrap its arguments and return
///     value through <see cref="Crypto.G9CHandshake.Seal(byte[], System.ReadOnlySpan{byte}, System.ReadOnlySpan{byte})"/>
///     using the per-connection session key derived from the handshake hub.
/// </summary>
/// <remarks>
///     <para>This attribute is declarative: it does not transparently encrypt arguments behind
///     the scenes. The library's threat model is "consumer opts in to ChaCha20-Poly1305 sealing"
///     because the arguments may carry binary blobs that already have their own framing. The
///     attribute exists so hub authors can document the contract and so future tooling (a
///     diagnostic analyzer, for example) can verify that decorated methods accept and return
///     <c>byte[]</c>.</para>
///     <para>Zero-cost: there is no runtime hook attached to this attribute.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class G9AttrEncryptedAttribute : Attribute
{
}
