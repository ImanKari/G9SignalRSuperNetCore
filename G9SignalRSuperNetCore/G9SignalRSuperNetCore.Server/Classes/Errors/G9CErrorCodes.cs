namespace G9SignalRSuperNetCore.Server.Classes.Errors;

/// <summary>
///     Stable, public error codes returned to clients when a hub policy rejects an invocation.
///     These string constants are part of the library's API surface; consumers can switch on them
///     to render localized error UIs or take recovery action.
/// </summary>
/// <remarks>
///     Codes use the prefix <c>G9_</c> to avoid collision with consumer codes. The codes are
///     wrapped in a <see cref="Microsoft.AspNetCore.SignalR.HubException"/> so they appear as
///     the message of the thrown exception on the client side. Clients can therefore inspect
///     <see cref="System.Exception.Message"/> and compare against these constants without
///     deserializing extra payloads.
/// </remarks>
public static class G9CErrorCodes
{
    /// <summary>The caller exceeded the per-method rate limit (<see cref="Attributes.G9AttrRateLimitAttribute"/>).</summary>
    public const string RateLimited = "G9_RATE_LIMITED";

    /// <summary>The caller exceeded the per-user or per-IP connection limit
    /// (<see cref="Attributes.G9AttrConnectionLimitAttribute"/>).</summary>
    public const string ConnectionLimit = "G9_CONNECTION_LIMIT";

    /// <summary>The connection is not in the <c>Connected</c> state for a method that requires it
    /// (<see cref="Attributes.G9AttrConnectionRequiredAttribute"/>).</summary>
    public const string ConnectionRequired = "G9_CONNECTION_REQUIRED";

    /// <summary>The authenticated principal does not carry the required role
    /// (<see cref="Attributes.G9AttrRequireRoleAttribute"/>).</summary>
    public const string RoleRequired = "G9_ROLE_REQUIRED";

    /// <summary>The authenticated principal does not carry the required claim value
    /// (<see cref="Attributes.G9AttrRequireClaimAttribute"/>).</summary>
    public const string ClaimRequired = "G9_CLAIM_REQUIRED";

    /// <summary>A file upload exceeded the configured maximum size.</summary>
    public const string UploadTooLarge = "G9_UPLOAD_TOO_LARGE";

    /// <summary>A file upload's declared SHA-256 hash did not match the bytes the server received.</summary>
    public const string UploadHashMismatch = "G9_UPLOAD_HASH_MISMATCH";

    /// <summary>A file upload referenced an unknown or already-completed upload id.</summary>
    public const string UploadUnknownId = "G9_UPLOAD_UNKNOWN_ID";

    /// <summary>A file upload failed for a reason other than the specific cases above (I/O, cancellation, etc.).</summary>
    public const string UploadFailed = "G9_UPLOAD_FAILED";
}
