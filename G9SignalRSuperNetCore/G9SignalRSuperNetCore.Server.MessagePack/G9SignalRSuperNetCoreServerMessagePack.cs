using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Nerdbank.MessagePack;
using Nerdbank.MessagePack.SignalR;
using PolyType;
using PolyType.Utilities;

namespace G9SignalRSuperNetCore.Server.MessagePack;

/// <summary>
///     Offers the binary MessagePack hub protocol next to JSON on a G9SignalRSuperNetCore server.
/// </summary>
/// <remarks>
///     <para>
///         SignalR negotiates the protocol per connection, so JSON clients keep working and clients move to MessagePack
///         one by one. The JSON protocol base64-encodes every <see cref="byte" />[] (+33% on the wire); MessagePack
///         carries bytes as bytes. Serializers come from PolyType shapes generated at compile time (NativeAOT-safe).
///     </para>
///     <para>
///         <see cref="HubOptions.MaximumReceiveMessageSize" /> counts protocol bytes, so the same limit admits ~33% larger
///         binary arguments over MessagePack than over JSON.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// builder.Services.AddSignalRSuperNetCoreCore();
/// builder.Services.AddG9SignalRSuperNetCoreMessagePack(ChatShapes.GeneratedTypeShapeProvider);
///     </code>
/// </example>
public static class G9SignalRSuperNetCoreServerMessagePack
{
    /// <summary>The shapes of the server library's own hub types (<see cref="G9CServerWireShapes" />).</summary>
    public static ITypeShapeProvider LibraryShapes => G9CServerWireShapes.GeneratedTypeShapeProvider;

    /// <summary>
    ///     The provider the protocol uses: <paramref name="appShapes" /> first, then <see cref="LibraryShapes" /> for
    ///     whatever the app did not declare.
    /// </summary>
    /// <param name="appShapes">The app's source-generated shapes (a <c>[GenerateShapeFor&lt;T&gt;]</c> witness), or <c>null</c>.</param>
    public static ITypeShapeProvider CombineShapes(ITypeShapeProvider? appShapes) =>
        appShapes is null ? LibraryShapes : new AggregatingTypeShapeProvider(appShapes, LibraryShapes);

    /// <summary>
    ///     Adds the MessagePack hub protocol to this SignalR server builder. JSON stays registered.
    /// </summary>
    /// <param name="builder">The builder returned by <c>services.AddSignalR()</c>.</param>
    /// <param name="appShapes">
    ///     Shapes for every parameter, return and stream-item type of the app's hub methods and client callbacks;
    ///     the library's own types are added automatically.
    /// </param>
    /// <param name="serializer">Optional serializer settings; both ends must agree on them.</param>
    public static ISignalRServerBuilder AddG9MessagePackProtocol(this ISignalRServerBuilder builder,
        ITypeShapeProvider? appShapes = null, MessagePackSerializer? serializer = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return serializer is null
            ? builder.AddMessagePackProtocol(CombineShapes(appShapes))
            : builder.AddMessagePackProtocol(CombineShapes(appShapes), serializer);
    }

    /// <summary>
    ///     Adds the MessagePack hub protocol to the app's SignalR services (same as
    ///     <c>services.AddSignalR().AddG9MessagePackProtocol(...)</c>). Call it next to <c>AddSignalRSuperNetCoreCore()</c>.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="appShapes">Shapes for the app's hub types; the library's own types are added automatically.</param>
    /// <param name="serializer">Optional serializer settings; both ends must agree on them.</param>
    public static IServiceCollection AddG9SignalRSuperNetCoreMessagePack(this IServiceCollection services,
        ITypeShapeProvider? appShapes = null, MessagePackSerializer? serializer = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSignalR().AddG9MessagePackProtocol(appShapes, serializer);
        return services;
    }
}
