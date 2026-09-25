namespace G9SignalRSuperNetCore.Server.Classes.Hubs;

/// <summary>
///     Options of the JWT authorize route served by <see cref="G9GetJwtHub"/> (2.9). Set them through the
///     <c>configureAuth</c> argument of <c>AddSignalRSuperNetCoreJwt</c>.
/// </summary>
/// <remarks>
///     The throttle is ON by default: every remote IP address may call <c>Authorize</c> in a burst of
///     <see cref="AuthorizeBurstPerIp"/> and then <see cref="AuthorizePerMinutePerIp"/> times a minute. A throttled call
///     is answered with <c>IsAccepted = false</c> and <c>RejectionReason = "G9_RATE_LIMITED"</c>, and the credential
///     check never runs, which is what makes password guessing through the auth route expensive.
/// </remarks>
public sealed class G9DtJwtAuthOptions
{
    /// <summary>Whether <c>Authorize</c> calls are throttled per remote IP address. Default: <c>true</c>.</summary>
    public bool ThrottleEnabled { get; set; } = true;

    /// <summary>Steady-state <c>Authorize</c> calls allowed per minute from one IP address. Default: 60. Values below 1 count as 1.</summary>
    public int AuthorizePerMinutePerIp { get; set; } = 60;

    /// <summary>Calls one IP address may make back to back before the per-minute rate applies. Default: 20. Values below 1 count as 1.</summary>
    public int AuthorizeBurstPerIp { get; set; } = 20;
}
