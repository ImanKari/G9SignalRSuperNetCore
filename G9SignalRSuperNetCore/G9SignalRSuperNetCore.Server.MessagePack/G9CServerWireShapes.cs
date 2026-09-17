using G9SignalRSuperNetCore.Server.Classes.FileUpload;
using G9SignalRSuperNetCore.Server.Classes.Presence;
using PolyType;

namespace G9SignalRSuperNetCore.Server.MessagePack;

/// <summary>
///     Build-time type shapes for every type the server library itself sends or receives over a hub: the resumable
///     file-transfer results and acknowledgements, presence events, binary chunks (client and server streams of
///     <see cref="byte" />[]), and the primitives those hub methods take.
/// </summary>
/// <remarks>
///     <see cref="G9SignalRSuperNetCoreServerMessagePack.AddG9MessagePackProtocol" /> chains these behind the app's own
///     shapes. <c>G9DtAuthorizeResult</c> is deliberately absent: the JWT auth hub works with untyped objects and stays
///     on JSON.
/// </remarks>
[GenerateShapeFor<G9DtBeginUploadResult>]
[GenerateShapeFor<G9DtUploadResult>]
[GenerateShapeFor<G9DtBeginDownloadResult>]
[GenerateShapeFor<G9DtUploadProgress>]
[GenerateShapeFor<G9DtPresenceEvent>]
// A no-result hub method is an acknowledged invocation from 2.7.0, and SignalR binds such a call's result
// as System.Object. Nothing is ever written for it - the completion carries no result - but the protocol
// resolves the converter before it knows that, so the shape has to exist. This is NOT a way to send
// untyped object payloads: the shape describes an opaque, memberless type.
[GenerateShapeFor<object>]
[GenerateShapeFor<byte[]>]
[GenerateShapeFor<string>]
[GenerateShapeFor<bool>]
[GenerateShapeFor<int>]
[GenerateShapeFor<long>]
public sealed partial class G9CServerWireShapes;
