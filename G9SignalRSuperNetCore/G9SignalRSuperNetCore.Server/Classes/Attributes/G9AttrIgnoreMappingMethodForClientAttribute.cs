namespace G9SignalRSuperNetCore.Server.Classes.Attributes;

/// <summary>
///     Specifies that a hub method should be excluded from client-side code generation.
///     When applied to a SignalR hub method, the build task generator will not include
///     this method in the generated client interface.
///     <remarks>
///         This attribute is used to hide server-side implementation details or internal methods
///         that should not be accessible from the client side. It only affects the code generation
///         process and does not impact runtime behavior.
///     </remarks>
///     <example>
///         <code>
/// public class CustomHub : G9AHubBase&lt;CustomHub, CustomClientInterface&gt;
/// {
///     // This method will not appear in generated client interface
///     [G9AttrExcludeFromClientGeneration]
///     public async Task InternalMethod()
///     {
///         // Implementation
///     }
///     // Other codes
/// }
/// </code>
///     </example>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class G9AttrExcludeFromClientGenerationAttribute : Attribute
{
}