using G9SignalRSuperNetCore.Server.Classes.Helper;
using Microsoft.AspNetCore.Authorization;

namespace G9SignalRSuperNetCore.Server.Classes.Attributes;

/// <summary>
///     An attribute that denies method access based on a custom authorization policy.
///     This attribute is used to apply the G9CAlwaysDenyRequirement policy, which always fails authorization.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class G9AttrDenyAccessAttribute : AuthorizeAttribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="G9AttrDenyAccessAttribute" /> class.
    ///     The policy is set to <see cref="G9CAlwaysDenyRequirement.DenyPolicyName" />, which will always deny access.
    /// </summary>
    public G9AttrDenyAccessAttribute()
    {
        // The policy name that always denies access.
        Policy = G9CAlwaysDenyRequirement.DenyPolicyName;
    }
}