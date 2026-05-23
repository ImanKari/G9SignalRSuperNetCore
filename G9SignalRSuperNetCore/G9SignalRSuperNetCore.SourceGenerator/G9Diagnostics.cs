using Microsoft.CodeAnalysis;

namespace G9SignalRSuperNetCore.SourceGenerator;

/// <summary>
///     Centralized diagnostic descriptors emitted by the G9 source generator.
///     Stable IDs across releases so consumers can suppress or filter individual rules.
/// </summary>
internal static class G9Diagnostics
{
    private const string Category = "G9SignalRSuperNetCore";

    /// <summary>G9001 — Hub method has unsupported return type.</summary>
    public static readonly DiagnosticDescriptor UnsupportedReturnType = new(
        id: "G9001",
        title: "Hub method has unsupported return type",
        messageFormat: "Hub method '{0}' returns '{1}' which is not supported by the typed proxy. " +
                       "Use Task, Task<T>, ValueTask, ValueTask<T>, or IAsyncEnumerable<T>.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The G9 source generator emits proxy code only for Task/ValueTask/IAsyncEnumerable returns. " +
                     "Methods with other return types are skipped from the typed Server proxy.");

    /// <summary>G9002 — Listener method must return Task or ValueTask.</summary>
    public static readonly DiagnosticDescriptor ListenerMustReturnTask = new(
        id: "G9002",
        title: "Listener method must return Task or ValueTask",
        messageFormat: "Listener method '{0}' on interface '{1}' returns '{2}'. " +
                       "Listener methods must return Task or ValueTask so the generator can wire them through HubConnection.On.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>G9003 — Hub method has too many parameters.</summary>
    public static readonly DiagnosticDescriptor TooManyParameters = new(
        id: "G9003",
        title: "Hub method has too many parameters",
        messageFormat: "Hub method '{0}' declares {1} parameters. SignalR's typed On overloads support up to 8 parameters; " +
                       "wrap the parameters in a DTO or split the method.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>G9004 — Generic hub method is not supported.</summary>
    public static readonly DiagnosticDescriptor GenericMethodNotSupported = new(
        id: "G9004",
        title: "Generic hub method is not supported",
        messageFormat: "Hub method '{0}' is generic. The typed proxy generator does not support generic hub methods; " +
                       "consider creating a non-generic facade.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>G9005 — Hub does not declare a route pattern.</summary>
    public static readonly DiagnosticDescriptor MissingRoutePattern = new(
        id: "G9005",
        title: "Hub does not declare a route pattern",
        messageFormat: "Hub '{0}' does not override RoutePattern() with a string-literal return. " +
                       "The generator falls back to '/{0}'; provide an explicit pattern for clarity.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    /// <summary>G9006 — Could not locate the listener interface.</summary>
    public static readonly DiagnosticDescriptor MissingListenerInterface = new(
        id: "G9006",
        title: "Could not locate the listener interface",
        messageFormat: "Hub '{0}' references listener interface '{1}', but the generator could not find its declaration in this compilation. " +
                       "The generated client class will compile, but no listener registrations will be emitted.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>G9007 — Internal generator error.</summary>
    public static readonly DiagnosticDescriptor InternalError = new(
        id: "G9007",
        title: "Internal generator error",
        messageFormat: "G9SignalRSuperNetCore source generator encountered an internal error: {0}. " +
                       "Please report this with a minimal repro at the repository's issue tracker.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
