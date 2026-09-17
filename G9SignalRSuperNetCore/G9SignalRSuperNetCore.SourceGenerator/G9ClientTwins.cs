using System;
using System.Linq;

namespace G9SignalRSuperNetCore.SourceGenerator;

/// <summary>
///     Rewrites the server library's file-transfer DTOs to their client-library twins in generated client code.
/// </summary>
/// <remarks>
///     <para>
///         A shared hub assembly references the SERVER package, so a hub's signatures name
///         <c>G9SignalRSuperNetCore.Server.Classes.FileUpload.*</c> types. The client library has identical twins in
///         <c>G9SignalRSuperNetCore.Client.FileUpload</c>, and <c>G9CFileUploader</c> / <c>G9CFileDownloader</c> use those.
///         Until 2.6 the generated client kept the server types, which broke two things: SignalR binds a callback's
///         arguments with the parameter types of the FIRST handler registered for it, so the generated
///         <c>UploadProgress(server type)</c> listener made the uploader's own acknowledgement handler fail its cast (no
///         acknowledged bytes were ever reported); and a binary (MessagePack) client needed type shapes for server-side
///         types. Both sides serialize the same member names, so the rewrite does not change the wire.
///     </para>
///     <para>
///         Applied only when the consuming compilation contains the client twins (the netstandard2.1 client build omits
///         the file-transfer types).
///     </para>
/// </remarks>
internal static class G9ClientTwins
{
    /// <summary>The client type whose presence switches the rewrite on.</summary>
    public const string ProbeMetadataName = "G9SignalRSuperNetCore.Client.FileUpload.G9DtUploadProgress";

    private const string ServerNamespace = "global::G9SignalRSuperNetCore.Server.Classes.FileUpload.";
    private const string ClientNamespace = "global::G9SignalRSuperNetCore.Client.FileUpload.";

    private static readonly string[] TwinNames =
    {
        "G9DtBeginUploadResult", "G9DtUploadResult", "G9DtBeginDownloadResult", "G9DtUploadProgress", "G9EUploadStatus"
    };

    public static HubModel Apply(HubModel model) => model with
    {
        ServerMethods = new EquatableArray<MethodModel>(model.ServerMethods.Select(method => method with
        {
            ReturnTypeFqName = Map(method.ReturnTypeFqName),
            UnwrappedTypeFqName = method.UnwrappedTypeFqName is null ? null : Map(method.UnwrappedTypeFqName),
            Parameters = MapParameters(method.Parameters)
        }).ToArray()),
        ListenerMethods = new EquatableArray<ListenerMethodModel>(model.ListenerMethods.Select(listener => listener with
        {
            Parameters = MapParameters(listener.Parameters)
        }).ToArray())
    };

    /// <summary>Replaces every whole occurrence of a twinned server type in a fully qualified type name.</summary>
    public static string Map(string typeName)
    {
        if (typeName.IndexOf(ServerNamespace, StringComparison.Ordinal) < 0) return typeName;
        foreach (var name in TwinNames) typeName = ReplaceWhole(typeName, ServerNamespace + name, ClientNamespace + name);
        return typeName;
    }

    private static EquatableArray<ParameterModel> MapParameters(EquatableArray<ParameterModel> parameters) =>
        new(parameters.Select(parameter => parameter with { TypeFqName = Map(parameter.TypeFqName) }).ToArray());

    private static string ReplaceWhole(string text, string from, string to)
    {
        var start = 0;
        while ((start = text.IndexOf(from, start, StringComparison.Ordinal)) >= 0)
        {
            var end = start + from.Length;
            if (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
            {
                start = end;
                continue;
            }

            text = text.Substring(0, start) + to + text.Substring(end);
            start += to.Length;
        }

        return text;
    }
}
