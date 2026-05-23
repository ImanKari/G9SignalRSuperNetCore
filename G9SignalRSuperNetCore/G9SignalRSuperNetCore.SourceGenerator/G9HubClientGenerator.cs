using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace G9SignalRSuperNetCore.SourceGenerator;

/// <summary>
///     Roslyn incremental source generator that emits a strongly-typed, AOT-safe SignalR client
///     class for every hub deriving from one of the G9 hub bases.
/// </summary>
/// <remarks>
///     <para>
///         The generator runs on every keystroke (incrementally) and emits source files into
///         <c>obj/Generated/G9SignalRSuperNetCore.SourceGenerator/</c>. The generated code uses
///         only Microsoft.AspNetCore.SignalR.Client.HubConnection primitives and contains no
///         runtime reflection or proxy generation.
///     </para>
///     <para>
///         Pipeline:
///         <list type="number">
///             <item>Find all class declarations whose base type chain contains a G9 hub base.</item>
///             <item>Project them to value-type <see cref="HubModel"/> records (so the cache works).</item>
///             <item>For each model, render a <see cref="G9Emitter"/>-produced compilation unit.</item>
///             <item>Report any model-level diagnostics back through <see cref="SourceProductionContext"/>.</item>
///         </list>
///     </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class G9HubClientGenerator : IIncrementalGenerator
{
    private static readonly string[] HubBaseNames =
    {
        "G9SignalRSuperNetCore.Server.Classes.Abstracts.G9AHubBase",
        "G9SignalRSuperNetCore.Server.Classes.Abstracts.G9AHubBaseWithJWTAuth",
        "G9SignalRSuperNetCore.Server.Classes.Abstracts.G9AHubBaseWithSession",
        "G9SignalRSuperNetCore.Server.Classes.Abstracts.G9AHubBaseWithSessionAndJWTAuth"
    };

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // The generator only emits the typed client when the consuming project also references
        // G9SignalRSuperNetCore.Client. This keeps server-only projects (e.g. the shared sample
        // library) free of generated client code that would not compile there.
        var hasClientLibrary = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName(
                "G9SignalRSuperNetCore.Client.G9SignalRSuperNetCoreClient`3") is not null);

        // (1) Hubs declared as syntax in the current compilation.
        var inCompilationHubs = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax cls && cls.BaseList is not null,
                transform: static (ctx, ct) =>
                {
                    var cls = (ClassDeclarationSyntax)ctx.Node;
                    var symbol = ctx.SemanticModel.GetDeclaredSymbol(cls, ct) as INamedTypeSymbol;
                    return symbol is null ? default : (Class: cls, Symbol: symbol);
                })
            .Where(static t => t.Symbol is not null && IsG9Hub(t.Symbol))
            .Select(static (t, ct) => SafeParseFromSyntax(t.Class, t.Symbol!, ct));

        // (2) Hubs that live in REFERENCED assemblies (e.g. shared sample library). The
        //     consuming console-client project doesn't carry the hub source, only the assembly,
        //     so we walk symbols of every reference once per compilation and produce models.
        var inReferencedHubs = context.CompilationProvider.SelectMany(static (compilation, ct) =>
        {
            var results = new System.Collections.Generic.List<HubModel>();
            foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                ct.ThrowIfCancellationRequested();
                CollectHubsFromAssembly(reference.GlobalNamespace, results, ct);
            }
            return results;
        });

        var combinedHubs = inCompilationHubs.Collect()
            .Combine(inReferencedHubs.Collect())
            .SelectMany(static (pair, _) =>
            {
                // Deduplicate by FullyQualifiedClassName: prefer the in-compilation model
                // because it has location-rich diagnostics.
                var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
                var output = new System.Collections.Generic.List<HubModel>();
                foreach (var m in pair.Left)
                    if (m is not null && seen.Add(m.FullyQualifiedClassName))
                        output.Add(m);
                foreach (var m in pair.Right)
                    if (m is not null && seen.Add(m.FullyQualifiedClassName))
                        output.Add(m);
                return output;
            });

        var combined = combinedHubs.Combine(hasClientLibrary);

        context.RegisterSourceOutput(combined, static (spc, pair) =>
        {
            var (model, hasClient) = pair;
            ReportDiagnostics(spc, model);

            // Emit the generated client only when the project consumes the client library.
            if (!hasClient) return;

            var fileName = SanitizeFileName(model.FullyQualifiedClassName) + ".g.cs";
            spc.AddSource(fileName, G9Emitter.Emit(model));
        });
    }

    private static HubModel? SafeParseFromSyntax(ClassDeclarationSyntax cls, INamedTypeSymbol symbol, System.Threading.CancellationToken ct)
    {
        try { return G9Parser.Parse(cls, symbol, ct); }
        catch (Exception ex)
        {
            return new HubModel(
                Namespace: string.Empty,
                ClassName: "G9_Internal_Error",
                FullyQualifiedClassName: symbol.ToDisplayString(),
                ListenerInterfaceFqName: string.Empty,
                ListenerInterfaceShortName: string.Empty,
                Kind: HubKind.Plain,
                RoutePattern: string.Empty,
                AuthRoutePattern: string.Empty,
                ServerMethods: EquatableArray<MethodModel>.Empty,
                ListenerMethods: EquatableArray<ListenerMethodModel>.Empty,
                Diagnostics: new EquatableArray<DiagnosticModel>(new[]
                {
                    new DiagnosticModel(G9Diagnostics.InternalError.Id,
                        ex.GetType().Name + ": " + ex.Message,
                        new DiagnosticLocation(string.Empty, 0, 0))
                }));
        }
    }

    private static void CollectHubsFromAssembly(
        INamespaceSymbol root,
        System.Collections.Generic.List<HubModel> sink,
        System.Threading.CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        foreach (var member in root.GetMembers())
        {
            if (member is INamespaceSymbol ns)
            {
                CollectHubsFromAssembly(ns, sink, ct);
            }
            else if (member is INamedTypeSymbol type
                     && type.DeclaredAccessibility == Accessibility.Public
                     && !type.IsAbstract
                     && IsG9Hub(type))
            {
                var model = G9Parser.ParseFromSymbol(type, ct);
                if (model is not null) sink.Add(model);
            }
        }
    }

    private static bool IsG9Hub(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            var openName = current.IsGenericType
                ? current.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            // Strip generic suffix for matching
            var atTick = openName.IndexOf('<');
            if (atTick > 0) openName = openName.Substring(0, atTick);
            // Trim global:: prefix
            if (openName.StartsWith("global::", StringComparison.Ordinal)) openName = openName.Substring(8);

            foreach (var hubBase in HubBaseNames)
                if (openName == hubBase) return true;
        }
        return false;
    }

    private static void ReportDiagnostics(SourceProductionContext spc, HubModel model)
    {
        if (model.Diagnostics.Count == 0) return;

        foreach (var d in model.Diagnostics)
        {
            var descriptor = ResolveDescriptor(d.Id);
            if (descriptor is null) continue;

            var location = string.IsNullOrEmpty(d.Location.FilePath)
                ? Location.None
                : Location.Create(
                    d.Location.FilePath,
                    Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(0, 0),
                    new Microsoft.CodeAnalysis.Text.LinePositionSpan(
                        new Microsoft.CodeAnalysis.Text.LinePosition(System.Math.Max(0, d.Location.Line - 1),
                                                                    System.Math.Max(0, d.Location.Column - 1)),
                        new Microsoft.CodeAnalysis.Text.LinePosition(System.Math.Max(0, d.Location.Line - 1),
                                                                    System.Math.Max(0, d.Location.Column - 1))));

            spc.ReportDiagnostic(Diagnostic.Create(descriptor, location, d.Message));
        }
    }

    private static DiagnosticDescriptor? ResolveDescriptor(string id) => id switch
    {
        "G9001" => G9Diagnostics.UnsupportedReturnType,
        "G9002" => G9Diagnostics.ListenerMustReturnTask,
        "G9003" => G9Diagnostics.TooManyParameters,
        "G9004" => G9Diagnostics.GenericMethodNotSupported,
        "G9005" => G9Diagnostics.MissingRoutePattern,
        "G9006" => G9Diagnostics.MissingListenerInterface,
        "G9007" => G9Diagnostics.InternalError,
        _ => null
    };

    private static string SanitizeFileName(string fullyQualified)
    {
        var sb = new System.Text.StringBuilder(fullyQualified.Length);
        foreach (var c in fullyQualified)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '.') sb.Append(c);
            else if (c == ':' || c == '<' || c == '>' || c == ',') sb.Append('_');
        }
        return sb.ToString();
    }
}
