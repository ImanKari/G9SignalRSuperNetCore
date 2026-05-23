using System.Collections.Generic;

namespace G9SignalRSuperNetCore.SourceGenerator;

/// <summary>
///     Per Andrew Lock's incremental-generator guidance, the generator pipeline must use
///     value-type or record models with structural equality so cached steps compare correctly.
///     These records are equatable by default (record types) and store only strings, so the
///     pipeline can short-circuit when nothing relevant changed.
/// </summary>
internal sealed record HubModel(
    string Namespace,
    string ClassName,
    string FullyQualifiedClassName,
    string ListenerInterfaceFqName,
    string ListenerInterfaceShortName,
    HubKind Kind,
    string RoutePattern,
    string AuthRoutePattern,
    EquatableArray<MethodModel> ServerMethods,
    EquatableArray<ListenerMethodModel> ListenerMethods,
    EquatableArray<DiagnosticModel> Diagnostics);

internal enum HubKind
{
    Plain,
    JwtAuth,
    Session,
    SessionAndJwtAuth
}

internal sealed record MethodModel(
    string Name,
    string ReturnTypeFqName,
    MethodReturnKind ReturnKind,
    string? UnwrappedTypeFqName,
    EquatableArray<ParameterModel> Parameters,
    string? XmlDocCommentXml);

internal sealed record ListenerMethodModel(
    string Name,
    string ReturnTypeFqName,
    ListenerReturnKind ReturnKind,
    EquatableArray<ParameterModel> Parameters,
    string? XmlDocCommentXml);

internal sealed record ParameterModel(
    string Name,
    string TypeFqName,
    bool IsCancellationToken);

internal sealed record DiagnosticModel(
    string Id,
    string Message,
    DiagnosticLocation Location);

internal sealed record DiagnosticLocation(string FilePath, int Line, int Column);

internal enum MethodReturnKind
{
    Task,
    TaskOfT,
    ValueTask,
    ValueTaskOfT,
    AsyncEnumerableOfT,
    Unsupported
}

internal enum ListenerReturnKind
{
    Task,
    ValueTask,
    Unsupported
}

/// <summary>
///     Cached value-equatable wrapper around an array. Required for incremental generators because
///     <see cref="System.Collections.Immutable.ImmutableArray{T}"/> uses reference equality which
///     would defeat the pipeline cache.
/// </summary>
internal readonly struct EquatableArray<T> : IReadOnlyList<T>
    where T : System.IEquatable<T>
{
    public static readonly EquatableArray<T> Empty = new(System.Array.Empty<T>());

    private readonly T[] _items;

    public EquatableArray(T[] items)
    {
        _items = items ?? System.Array.Empty<T>();
    }

    public int Count => _items.Length;
    public T this[int index] => _items[index];

    public IEnumerator<T> GetEnumerator()
    {
        foreach (var item in _items) yield return item;
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var item in _items)
                hash = (hash * 31) + (item?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public bool Equals(EquatableArray<T> other)
    {
        if (_items.Length != other._items.Length) return false;
        for (var i = 0; i < _items.Length; i++)
            if (!_items[i].Equals(other._items[i])) return false;
        return true;
    }

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);
    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}
