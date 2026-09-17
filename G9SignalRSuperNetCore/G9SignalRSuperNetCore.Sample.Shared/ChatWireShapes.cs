using G9SignalRSuperNetCore.Server.Classes.Presence;
using PolyType;

namespace G9SignalRSuperNetCore.Sample.Shared;

/// <summary>
///     Build-time type shapes for the MessagePack hub protocol: every parameter, return and stream-item type of
///     <see cref="ChatHub" /> and <see cref="IChatClient" />. The file-transfer DTOs are not listed: the
///     <c>*.MessagePack</c> packages add the library's own shapes behind these.
/// </summary>
[GenerateShapeFor<List<string>>]
[GenerateShapeFor<string>]
[GenerateShapeFor<int>]
[GenerateShapeFor<long>]
[GenerateShapeFor<bool>]
[GenerateShapeFor<byte[]>]
[GenerateShapeFor<G9DtPresenceEvent>]
public sealed partial class ChatWireShapes;
