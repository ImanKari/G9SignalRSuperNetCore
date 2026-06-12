// Polyfills required only for the netstandard2.1 (Unity 6) build.
// On net5.0+ these types already exist in the BCL, so they are excluded there to avoid conflicts.
#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Enables C# 9+ <c>init</c>-only setters and positional <c>record</c> / <c>record struct</c>
    /// types on target frameworks (netstandard2.1) whose BCL does not ship this type.
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
#endif
