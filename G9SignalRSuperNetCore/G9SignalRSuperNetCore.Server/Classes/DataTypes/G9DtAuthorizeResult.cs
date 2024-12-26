namespace G9SignalRSuperNetCore.Server.Classes.DataTypes;

/// <summary>
///     Represents the result of an authorization process.
///     Contains details about whether the authorization was accepted, the reason for rejection if any,
///     the JWT token issued by the authentication server, and any extra data that the server might send.
/// </summary>
public class G9DtAuthorizeResult
{
    /// <summary>
    ///     Gets or sets a value indicating whether the authorization was accepted.
    /// </summary>
    /// <value>
    ///     <c>true</c> if the authorization was accepted; otherwise, <c>false</c>.
    /// </value>
    public bool IsAccepted { get; set; }

    /// <summary>
    ///     Gets or sets the reason for rejection, if the authorization was not accepted.
    /// </summary>
    /// <value>
    ///     A string representing the rejection reason, or <c>null</c> if not applicable.
    /// </value>
    public string? RejectionReason { get; set; }

    /// <summary>
    ///     Gets or sets the JWT token issued by the authentication server, if the authorization was accepted.
    /// </summary>
    /// <value>
    ///     A string representing the JWT token, or <c>null</c> if the authorization was not accepted or a token was not
    ///     issued.
    /// </value>
    public string? JWToken { get; set; }

    /// <summary>
    ///     Gets or sets any extra data that the server may send along with the authorization result.
    ///     This data is optional and can contain additional context or information.
    /// </summary>
    /// <value>
    ///     An object representing any additional data, or <c>null</c> if no extra data is provided.
    /// </value>
    public object? ExtraData { get; set; }
}