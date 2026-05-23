using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace G9SignalRSuperNetCore.SourceGenerator;

/// <summary>
///     Converts a hub class declaration (Roslyn syntax + symbol) into a value-type
///     <see cref="HubModel"/> that the emitter consumes. All decisions about what is and is not
///     supported live here so the emitter can stay simple.
/// </summary>
internal static class G9Parser
{
    private const string ExcludeAttributeName = "G9AttrExcludeFromClientGenerationAttribute";
    private const string DenyAccessAttributeName = "G9AttrDenyAccessAttribute";

    private static readonly HashSet<string> FrameworkExcludedMethods = new(System.StringComparer.Ordinal)
    {
        "RoutePattern", "AuthAndGetJWTRoutePattern", "GetAuthorizeTokenValidationForHub",
        "ConfigureHub", "ConfigureHubOption", "ConfigureHubForJWTRoute",
        "OnConnectedAsync", "OnDisconnectedAsync", "OnConnectedAsyncNext", "OnDisconnectedAsyncNext",
        "AuthenticateAndGenerateJwtTokenAsync", "IsUserConnected", "CleanupExpiredSessions",
        "Dispose", "DisposeAsync"
    };

    /// <summary>
    ///     Parses a hub from its symbol alone (no syntax). Used when the generator finds a
    ///     hub class in a referenced assembly. Route patterns are read from compile-time
    ///     constants on the type when present (a declared <c>public const string Route = "..."</c>
    ///     is the convention) and otherwise default sensibly.
    /// </summary>
    public static HubModel? ParseFromSymbol(INamedTypeSymbol hubSymbol, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var diagnostics = new List<DiagnosticModel>();
        var kind = ClassifyHub(hubSymbol);
        if (kind is null) return null;

        var listenerInterface = ExtractListenerInterface(hubSymbol);
        if (listenerInterface is null) return null;

        var routePattern = ExtractStringConstantOrFallback(hubSymbol, "Route", "/" + hubSymbol.Name);
        var authRoutePattern = kind is HubKind.JwtAuth or HubKind.SessionAndJwtAuth
            ? ExtractStringConstantOrFallback(hubSymbol, "AuthRoute", "/AuthHub")
            : string.Empty;

        var serverMethods = ParseServerMethods(hubSymbol, ct, diagnostics);
        var listenerMethods = ParseListenerMethods(hubSymbol, listenerInterface, ct, diagnostics);

        return new HubModel(
            Namespace: hubSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : hubSymbol.ContainingNamespace.ToDisplayString(),
            ClassName: hubSymbol.Name,
            FullyQualifiedClassName: hubSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ListenerInterfaceFqName: listenerInterface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ListenerInterfaceShortName: listenerInterface.Name,
            Kind: kind.Value,
            RoutePattern: routePattern,
            AuthRoutePattern: authRoutePattern,
            ServerMethods: new EquatableArray<MethodModel>(serverMethods.ToArray()),
            ListenerMethods: new EquatableArray<ListenerMethodModel>(listenerMethods.ToArray()),
            Diagnostics: new EquatableArray<DiagnosticModel>(diagnostics.ToArray()));
    }

    private static string ExtractStringConstantOrFallback(INamedTypeSymbol type, string fieldName, string fallback)
    {
        foreach (var member in type.GetMembers(fieldName))
        {
            if (member is IFieldSymbol field && field.IsConst && field.HasConstantValue && field.ConstantValue is string s)
                return s;
        }
        return fallback;
    }

    public static HubModel? Parse(ClassDeclarationSyntax hubSyntax, INamedTypeSymbol hubSymbol, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var diagnostics = new List<DiagnosticModel>();

        var kind = ClassifyHub(hubSymbol);
        if (kind is null) return null;

        var listenerInterface = ExtractListenerInterface(hubSymbol);
        if (listenerInterface is null) return null;

        var routePattern = ExtractStringFromOverride(hubSyntax, "RoutePattern", "/" + hubSymbol.Name, diagnostics);
        var authRoutePattern = kind is HubKind.JwtAuth or HubKind.SessionAndJwtAuth
            ? ExtractStringFromOverride(hubSyntax, "AuthAndGetJWTRoutePattern", "/AuthHub", diagnostics)
            : string.Empty;

        var serverMethods = ParseServerMethods(hubSymbol, ct, diagnostics);
        var listenerMethods = ParseListenerMethods(hubSymbol, listenerInterface, ct, diagnostics);

        return new HubModel(
            Namespace: hubSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : hubSymbol.ContainingNamespace.ToDisplayString(),
            ClassName: hubSymbol.Name,
            FullyQualifiedClassName: hubSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ListenerInterfaceFqName: listenerInterface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ListenerInterfaceShortName: listenerInterface.Name,
            Kind: kind.Value,
            RoutePattern: routePattern,
            AuthRoutePattern: authRoutePattern,
            ServerMethods: new EquatableArray<MethodModel>(serverMethods.ToArray()),
            ListenerMethods: new EquatableArray<ListenerMethodModel>(listenerMethods.ToArray()),
            Diagnostics: new EquatableArray<DiagnosticModel>(diagnostics.ToArray()));
    }

    private static HubKind? ClassifyHub(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            var name = current.Name;
            if (name == "G9AHubBaseWithSessionAndJWTAuth") return HubKind.SessionAndJwtAuth;
            if (name == "G9AHubBaseWithJWTAuth") return HubKind.JwtAuth;
            if (name == "G9AHubBaseWithSession") return HubKind.Session;
            if (name == "G9AHubBase") return HubKind.Plain;
        }

        return null;
    }

    private static INamedTypeSymbol? ExtractListenerInterface(INamedTypeSymbol hubSymbol)
    {
        for (var current = hubSymbol.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType &&
                (current.Name == "G9AHubBase" || current.Name == "G9AHubBaseWithJWTAuth" ||
                 current.Name == "G9AHubBaseWithSession" || current.Name == "G9AHubBaseWithSessionAndJWTAuth") &&
                current.TypeArguments.Length >= 2)
            {
                return current.TypeArguments[1] as INamedTypeSymbol;
            }
        }

        return null;
    }

    private static List<MethodModel> ParseServerMethods(
        INamedTypeSymbol hub,
        CancellationToken ct,
        List<DiagnosticModel> diagnostics)
    {
        var result = new List<MethodModel>();

        foreach (var member in hub.GetMembers().OfType<IMethodSymbol>())
        {
            ct.ThrowIfCancellationRequested();

            if (member.MethodKind != MethodKind.Ordinary) continue;
            if (member.DeclaredAccessibility != Accessibility.Public) continue;
            if (member.IsStatic) continue;
            if (FrameworkExcludedMethods.Contains(member.Name)) continue;
            if (HasAttribute(member, ExcludeAttributeName)) continue;
            if (HasAttribute(member, DenyAccessAttributeName)) continue;

            if (member.IsGenericMethod)
            {
                diagnostics.Add(MakeDiagnostic(G9Diagnostics.GenericMethodNotSupported, member, member.Name));
                continue;
            }

            if (member.Parameters.Length > 8)
            {
                diagnostics.Add(MakeDiagnostic(G9Diagnostics.TooManyParameters, member,
                    member.Name, member.Parameters.Length.ToString()));
                continue;
            }

            var (returnKind, unwrapped) = ClassifyServerReturn(member.ReturnType);
            if (returnKind == MethodReturnKind.Unsupported)
            {
                diagnostics.Add(MakeDiagnostic(G9Diagnostics.UnsupportedReturnType, member,
                    member.Name, member.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                continue;
            }

            var parameters = member.Parameters
                .Select(p => new ParameterModel(
                    Name: p.Name,
                    TypeFqName: p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    IsCancellationToken: p.Type.ToDisplayString() == "System.Threading.CancellationToken"))
                .ToArray();

            result.Add(new MethodModel(
                Name: member.Name,
                ReturnTypeFqName: member.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ReturnKind: returnKind,
                UnwrappedTypeFqName: unwrapped,
                Parameters: new EquatableArray<ParameterModel>(parameters),
                XmlDocCommentXml: member.GetDocumentationCommentXml(cancellationToken: ct)));
        }

        return result;
    }

    private static List<ListenerMethodModel> ParseListenerMethods(
        INamedTypeSymbol hub,
        INamedTypeSymbol listenerInterface,
        CancellationToken ct,
        List<DiagnosticModel> diagnostics)
    {
        var result = new List<ListenerMethodModel>();

        foreach (var member in listenerInterface.GetMembers().OfType<IMethodSymbol>())
        {
            ct.ThrowIfCancellationRequested();

            if (member.MethodKind != MethodKind.Ordinary) continue;

            if (member.IsGenericMethod)
            {
                diagnostics.Add(MakeDiagnostic(G9Diagnostics.GenericMethodNotSupported, member, member.Name));
                continue;
            }

            if (member.Parameters.Length > 8)
            {
                diagnostics.Add(MakeDiagnostic(G9Diagnostics.TooManyParameters, member,
                    member.Name, member.Parameters.Length.ToString()));
                continue;
            }

            var returnKind = ClassifyListenerReturn(member.ReturnType);
            if (returnKind == ListenerReturnKind.Unsupported)
            {
                diagnostics.Add(MakeDiagnostic(G9Diagnostics.ListenerMustReturnTask, member,
                    member.Name,
                    listenerInterface.ToDisplayString(),
                    member.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                continue;
            }

            var parameters = member.Parameters
                .Select(p => new ParameterModel(
                    Name: p.Name,
                    TypeFqName: p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    IsCancellationToken: false))
                .ToArray();

            result.Add(new ListenerMethodModel(
                Name: member.Name,
                ReturnTypeFqName: member.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ReturnKind: returnKind,
                Parameters: new EquatableArray<ParameterModel>(parameters),
                XmlDocCommentXml: member.GetDocumentationCommentXml(cancellationToken: ct)));
        }

        return result;
    }

    private static (MethodReturnKind kind, string? unwrapped) ClassifyServerReturn(ITypeSymbol returnType)
    {
        var displayString = returnType.ToDisplayString();

        if (displayString == "System.Threading.Tasks.Task") return (MethodReturnKind.Task, null);
        if (displayString == "System.Threading.Tasks.ValueTask") return (MethodReturnKind.ValueTask, null);

        if (returnType is INamedTypeSymbol named && named.IsGenericType && named.TypeArguments.Length == 1)
        {
            var openName = named.ConstructedFrom.ToDisplayString();
            var argFq = named.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (openName == "System.Threading.Tasks.Task<TResult>") return (MethodReturnKind.TaskOfT, argFq);
            if (openName == "System.Threading.Tasks.ValueTask<TResult>") return (MethodReturnKind.ValueTaskOfT, argFq);
            if (openName == "System.Collections.Generic.IAsyncEnumerable<T>") return (MethodReturnKind.AsyncEnumerableOfT, argFq);
        }

        return (MethodReturnKind.Unsupported, null);
    }

    private static ListenerReturnKind ClassifyListenerReturn(ITypeSymbol returnType)
    {
        var displayString = returnType.ToDisplayString();
        if (displayString == "System.Threading.Tasks.Task") return ListenerReturnKind.Task;
        if (displayString == "System.Threading.Tasks.ValueTask") return ListenerReturnKind.ValueTask;
        return ListenerReturnKind.Unsupported;
    }

    private static bool HasAttribute(IMethodSymbol method, string attributeShortName)
    {
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.Name == attributeShortName) return true;
        }
        return false;
    }

    private static string ExtractStringFromOverride(
        ClassDeclarationSyntax cls,
        string methodName,
        string fallback,
        List<DiagnosticModel> diagnostics)
    {
        var method = cls.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == methodName && m.ParameterList.Parameters.Count == 0);

        if (method is null)
        {
            if (methodName == "RoutePattern")
                diagnostics.Add(new DiagnosticModel(
                    Id: G9Diagnostics.MissingRoutePattern.Id,
                    Message: cls.Identifier.Text,
                    Location: ToLocation(cls.Identifier.GetLocation())));
            return fallback;
        }

        if (method.Body is not null)
        {
            var ret = method.Body.Statements.OfType<ReturnStatementSyntax>().FirstOrDefault();
            if (ret?.Expression is LiteralExpressionSyntax lit &&
                lit.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression))
                return lit.Token.ValueText;
        }
        else if (method.ExpressionBody?.Expression is LiteralExpressionSyntax expLit &&
                 expLit.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression))
        {
            return expLit.Token.ValueText;
        }

        return fallback;
    }

    private static DiagnosticModel MakeDiagnostic(DiagnosticDescriptor descriptor, ISymbol symbol, params string[] args)
    {
        var location = symbol.Locations.FirstOrDefault() ?? Location.None;
        return new DiagnosticModel(
            Id: descriptor.Id,
            Message: string.Format(descriptor.MessageFormat.ToString(), args),
            Location: ToLocation(location));
    }

    private static DiagnosticLocation ToLocation(Location location)
    {
        if (location == Location.None) return new DiagnosticLocation(string.Empty, 0, 0);
        var span = location.GetLineSpan();
        return new DiagnosticLocation(
            FilePath: span.Path ?? string.Empty,
            Line: span.StartLinePosition.Line + 1,
            Column: span.StartLinePosition.Character + 1);
    }
}
