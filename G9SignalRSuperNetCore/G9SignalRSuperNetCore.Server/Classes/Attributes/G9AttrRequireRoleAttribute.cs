namespace G9SignalRSuperNetCore.Server.Classes.Attributes;

/// <summary>
///     Requires that the authenticated principal carry one of the listed roles.
///     Equivalent to <c>[Authorize(Roles = "...")]</c> but integrates with G9's stable error codes.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class G9AttrRequireRoleAttribute : Attribute
{
    /// <summary>Roles accepted as proof. The caller must carry at least one.</summary>
    public string[] Roles { get; }

    /// <summary>Initializes a new role requirement.</summary>
    /// <param name="roles">One or more accepted roles.</param>
    public G9AttrRequireRoleAttribute(params string[] roles)
    {
        if (roles is null || roles.Length == 0) throw new ArgumentException("At least one role is required.", nameof(roles));
        Roles = roles;
    }
}
