using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using G9SignalRSuperNetCore.Server.Enums;
using Microsoft.IdentityModel.Tokens;

namespace G9SignalRSuperNetCore.Server.Classes.Helper;

/// <summary>
///     Factory class for creating JSON Web Tokens (JWTs) and associated validation parameters.
/// </summary>
/// <remarks>
///     A single <see cref="JwtSecurityTokenHandler"/> is reused process-wide.
///     The handler is documented as thread-safe for token issuance and validation.
/// </remarks>
public sealed class G9JWTokenFactory
{
    private static readonly JwtSecurityTokenHandler SharedTokenHandler = new();

    #region Fields

    /// <summary>
    ///     Specifies the status of rejection.
    /// </summary>
    public readonly bool IsRejected;

    /// <summary>
    ///     The generated JWT as a string.
    /// </summary>
    public readonly string? JWToken;

    /// <summary>
    ///     Specifies the rejection reason.
    /// </summary>
    public readonly string? RejectionReason;

    /// <summary>
    ///     The validation parameters used to validate the generated JWT.
    /// </summary>
    public readonly TokenValidationParameters? ValidationParameters;

    #endregion

    #region Constructors

    private G9JWTokenFactory(string jwToken, TokenValidationParameters validationParameters)
    {
        JWToken = jwToken;
        ValidationParameters = validationParameters;
    }

    private G9JWTokenFactory(string? rejectionReason)
    {
        IsRejected = true;
        RejectionReason = rejectionReason;
        JWToken = null;
        ValidationParameters = null;
    }

    #endregion

    #region Helpers

    private static (SymmetricSecurityKey, TokenValidationParameters) CreateKeyAndValidationParameters(
        string jwtSecret,
        string issuer,
        string audience,
        bool requireExpirationTime,
        TimeSpan? clockSkew = null,
        bool validateTokenReplay = false,
        Func<DateTime?, DateTime?, SecurityToken, TokenValidationParameters, bool>? lifetimeValidator = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = key,
            ValidateLifetime = true,
            RequireExpirationTime = requireExpirationTime,
            ClockSkew = clockSkew ?? TimeSpan.FromMinutes(5),
            LifetimeValidator = lifetimeValidator != null
                ? new LifetimeValidator((notBefore, expires, securityToken, validationParams) =>
                    lifetimeValidator(notBefore, expires, securityToken, validationParams))
                : null
        };

        if (validateTokenReplay) validationParameters.ValidateTokenReplay = true;

        return (key, validationParameters);
    }

    private static G9JWTokenFactory WriteToken(
        SymmetricSecurityKey key,
        TokenValidationParameters validationParameters,
        string issuer,
        string audience,
        IList<Claim>? claims,
        DateTime? notBefore,
        DateTime? expires,
        G9ESecurityAlgorithms securityAlgorithm)
    {
        var credentials = new SigningCredentials(key, securityAlgorithm.ToSecurityAlgorithm());
        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            notBefore,
            expires,
            credentials);
        var jwt = SharedTokenHandler.WriteToken(token);
        return new G9JWTokenFactory(jwt, validationParameters);
    }

    #endregion

    #region Public factory methods

    /// <summary>
    ///     Generates a JWT token with default claims.
    /// </summary>
    public static G9JWTokenFactory GenerateJWTToken(
        string jwtSecret, string issuer, string audience, DateTime? expires = null,
        G9ESecurityAlgorithms securityAlgorithm = G9ESecurityAlgorithms.HmacSha256)
    {
        var (key, parameters) = CreateKeyAndValidationParameters(jwtSecret, issuer, audience, expires != null);
        return WriteToken(key, parameters, issuer, audience, null, null, expires, securityAlgorithm);
    }

    /// <summary>
    ///     Generates a JWT token with a username claim.
    /// </summary>
    public static G9JWTokenFactory GenerateJWTToken(
        string jwtSecret, string username, string issuer, string audience, DateTime? expires = null,
        G9ESecurityAlgorithms securityAlgorithm = G9ESecurityAlgorithms.HmacSha256)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        return GenerateJWTToken(jwtSecret, issuer, audience, claims, expires, securityAlgorithm);
    }

    /// <summary>
    ///     Generates a JWT token with username and role claims.
    /// </summary>
    public static G9JWTokenFactory GenerateJWTToken(
        string jwtSecret, string username, string role, string issuer, string audience, DateTime? expires = null,
        G9ESecurityAlgorithms securityAlgorithm = G9ESecurityAlgorithms.HmacSha256)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Sub, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        return GenerateJWTToken(jwtSecret, issuer, audience, claims, expires, securityAlgorithm);
    }

    /// <summary>
    ///     Generates a JWT with the specified claims and signing parameters.
    /// </summary>
    public static G9JWTokenFactory GenerateJWTToken(
        string jwtSecret, string issuer, string audience, IList<Claim> claims, DateTime? expires = null,
        G9ESecurityAlgorithms securityAlgorithm = G9ESecurityAlgorithms.HmacSha256)
    {
        var (key, parameters) = CreateKeyAndValidationParameters(jwtSecret, issuer, audience, expires != null);
        return WriteToken(key, parameters, issuer, audience, claims, null, expires, securityAlgorithm);
    }

    /// <summary>
    ///     Generates a JWT with full control over expiration and validation.
    /// </summary>
    public static G9JWTokenFactory GenerateJWTToken(
        string jwtSecret,
        string issuer,
        string audience,
        IList<Claim>? claims = null,
        string? username = null,
        string? role = null,
        DateTime? notBefore = null,
        DateTime? expires = null,
        TimeSpan? clockSkew = null,
        bool validateTokenReplay = false,
        Func<DateTime?, DateTime?, SecurityToken, TokenValidationParameters, bool>? lifetimeValidator = null,
        G9ESecurityAlgorithms securityAlgorithm = G9ESecurityAlgorithms.HmacSha256)
    {
        var tokenClaims = claims != null ? new List<Claim>(claims) : new List<Claim>();

        if (!string.IsNullOrEmpty(username))
        {
            tokenClaims.Add(new Claim(JwtRegisteredClaimNames.Sub, username));
            tokenClaims.Add(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()));
        }

        if (!string.IsNullOrEmpty(role)) tokenClaims.Add(new Claim(ClaimTypes.Role, role));

        var (key, parameters) = CreateKeyAndValidationParameters(
            jwtSecret, issuer, audience, expires != null, clockSkew, validateTokenReplay, lifetimeValidator);

        return WriteToken(key, parameters, issuer, audience,
            tokenClaims.Count > 0 ? tokenClaims : null,
            notBefore, expires, securityAlgorithm);
    }

    /// <summary>
    ///     Returns a factory representing a rejected authorization with the supplied reason.
    /// </summary>
    public static G9JWTokenFactory RejectAuthorize(string? rejectionReason) => new(rejectionReason);

    #endregion
}
