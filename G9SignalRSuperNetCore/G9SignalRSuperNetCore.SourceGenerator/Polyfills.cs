// Polyfills required because the source generator targets netstandard2.0
// (a Roslyn requirement) but uses C# 9+ language features.

namespace System.Runtime.CompilerServices;

/// <summary>Reserved compiler infrastructure type required for record <c>init</c> accessors.</summary>
internal static class IsExternalInit { }
