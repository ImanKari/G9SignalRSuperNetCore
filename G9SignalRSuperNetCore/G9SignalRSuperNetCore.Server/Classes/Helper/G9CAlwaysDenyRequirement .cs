using Microsoft.AspNetCore.Authorization;

namespace G9SignalRSuperNetCore.Server.Classes.Helper;

/// <summary>
///     A custom authorization requirement that is used to mark a policy
///     as one that will always fail the authorization process.
/// </summary>
public class G9CAlwaysDenyRequirement : IAuthorizationRequirement
{
    public const string DenyPolicyName = "[G9AlwaysDenyG9]";
    // This class doesn't hold any logic. It's simply a marker used to indicate
    // that authorization should always fail. It implements IAuthorizationRequirement
    // to integrate with ASP.NET Core's authorization system.
}

/// <summary>
///     A custom authorization handler that will always fail the authorization
///     process for any incoming request that checks for the <see cref="G9CAlwaysDenyRequirement" />.
/// </summary>
public class G9CAlwaysDenyHandler : AuthorizationHandler<G9CAlwaysDenyRequirement>
{
    /// <summary>
    ///     Handles the authorization requirement by forcing it to fail.
    /// </summary>
    /// <param name="context">
    ///     The context for the current authorization request, which contains
    ///     the details about the user and the resource being accessed.
    /// </param>
    /// <param name="requirement">
    ///     The authorization requirement being evaluated. In this case, it will always be
    ///     of type <see cref="G9CAlwaysDenyRequirement" />.
    /// </param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
        G9CAlwaysDenyRequirement requirement)
    {
        // Explicitly fail the authorization for any request with the G9CAlwaysDenyRequirement.
        context.Fail();
        return Task.CompletedTask; // Authorization is rejected, so we complete the task immediately.
    }
}