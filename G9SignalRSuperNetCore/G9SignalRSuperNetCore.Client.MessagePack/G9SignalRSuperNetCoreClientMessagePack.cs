using Microsoft.AspNetCore.SignalR.Client;
using Nerdbank.MessagePack;
using Nerdbank.MessagePack.SignalR;
using PolyType;
using PolyType.Utilities;

namespace G9SignalRSuperNetCore.Client.MessagePack;

/// <summary>
///     Switches a G9SignalRSuperNetCore client (or any <see cref="IHubConnectionBuilder" />) to the binary MessagePack
///     hub protocol.
/// </summary>
/// <remarks>
///     <para>
///         Why: the JSON protocol base64-encodes every <see cref="byte" />[] (+33% on the wire) and parses text. The
///         MessagePack protocol carries bytes as bytes. Nerdbank.MessagePack builds its serializers from PolyType type
///         shapes generated at compile time, so the protocol stays NativeAOT- and trim-safe (Microsoft's MessagePack
///         protocol is not).
///     </para>
///     <para>
///         The server must offer the protocol too (<c>G9SignalRSuperNetCore.Server.MessagePack</c>); a server that
///         only speaks JSON refuses the handshake. Keep the JWT auth connection on JSON: <c>G9GetJwtHub.Authorize</c>
///         takes an untyped <see cref="object" />, which has no type shape.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [GenerateShapeFor&lt;ChatMessage&gt;]
/// public partial class ChatShapes;
///
/// var client = new ChatHubClient(url, customConfigureBuilder: b =&gt; b.AddG9MessagePackProtocol(ChatShapes.GeneratedTypeShapeProvider));
///     </code>
/// </example>
public static class G9SignalRSuperNetCoreClientMessagePack
{
    /// <summary>The shapes of the client library's own hub types (<see cref="G9CClientWireShapes" />).</summary>
    public static ITypeShapeProvider LibraryShapes => G9CClientWireShapes.GeneratedTypeShapeProvider;

    /// <summary>
    ///     The provider the protocol uses: <paramref name="appShapes" /> first, then <see cref="LibraryShapes" /> for
    ///     whatever the app did not declare.
    /// </summary>
    /// <param name="appShapes">The app's source-generated shapes (a <c>[GenerateShapeFor&lt;T&gt;]</c> witness), or <c>null</c>.</param>
    public static ITypeShapeProvider CombineShapes(ITypeShapeProvider? appShapes) =>
        appShapes is null ? LibraryShapes : new AggregatingTypeShapeProvider(appShapes, LibraryShapes);

    /// <summary>
    ///     Uses the MessagePack hub protocol on this connection.
    /// </summary>
    /// <param name="builder">The connection builder (the <c>customConfigureBuilder</c> callback of a G9 client).</param>
    /// <param name="appShapes">
    ///     Shapes for every parameter, return and stream-item type of the app's hub methods and client listeners;
    ///     the library's own types are added automatically.
    /// </param>
    /// <param name="serializer">Optional serializer settings; both ends must agree on them.</param>
    public static IHubConnectionBuilder AddG9MessagePackProtocol(this IHubConnectionBuilder builder,
        ITypeShapeProvider? appShapes = null, MessagePackSerializer? serializer = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return serializer is null
            ? builder.AddMessagePackProtocol(CombineShapes(appShapes))
            : builder.AddMessagePackProtocol(CombineShapes(appShapes), serializer);
    }
}
