using Microsoft.AspNetCore.SignalR;

namespace G9SignalRSuperNetCore.Server.Classes.Authorization;

/// <summary>
///     Decides whether the caller of a hub method holds an application-defined permission declared with
///     <see cref="Attributes.G9AttrRequirePermissionAttribute"/> (2.9).
/// </summary>
/// <remarks>
///     <para>Register it in DI with any lifetime; it is resolved per invocation from
///     <see cref="HubInvocationContext.ServiceProvider"/>, so a scoped implementation may depend on a scoped
///     <c>DbContext</c>. It is called once per required permission, and all of them must be granted.</para>
///     <code>
///         builder.Services.AddScoped&lt;IG9HubPermissionHandler, MyPermissionHandler&gt;();
///     </code>
/// </remarks>
public interface IG9HubPermissionHandler
{
    /// <summary>Returns <c>true</c> when the caller of <paramref name="context"/> holds <paramref name="permission"/>.</summary>
    /// <param name="context">The invocation being authorized: caller, hub, method and arguments.</param>
    /// <param name="permission">The permission named by <see cref="Attributes.G9AttrRequirePermissionAttribute.Permission"/>.</param>
    ValueTask<bool> IsAllowedAsync(HubInvocationContext context, string permission);
}
