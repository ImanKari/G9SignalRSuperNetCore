namespace G9SignalRSuperNetCore.Server.Classes.Attributes;

/// <summary>
///     Requires an application-defined permission before a hub method runs (2.9). The check itself is delegated to the
///     <see cref="Authorization.IG9HubPermissionHandler"/> the application registers in DI, so permissions can come from
///     anywhere: a database, a claims transformation, a feature-flag service.
/// </summary>
/// <remarks>
///     <para>Stackable and allowed on the hub class and on methods: every permission declared on the method AND on its
///     hub class (including base classes) must be granted. A denial is reported to the caller as a
///     <see cref="Microsoft.AspNetCore.SignalR.HubException"/> whose message is
///     <see cref="Errors.G9CErrorCodes.PermissionRequired"/>.</para>
///     <para>Evaluated by <see cref="Filters.G9CHubFilter"/> after the role and claim checks and before the rate limit.
///     A method that requires a permission while no <see cref="Authorization.IG9HubPermissionHandler"/> is registered
///     is refused (fail closed) and a warning is logged.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class G9AttrRequirePermissionAttribute(string permission) : Attribute
{
    /// <summary>The permission the caller must hold, passed verbatim to <see cref="Authorization.IG9HubPermissionHandler.IsAllowedAsync"/>.</summary>
    public string Permission { get; } = string.IsNullOrEmpty(permission)
        ? throw new ArgumentException("A permission name is required.", nameof(permission))
        : permission;
}
