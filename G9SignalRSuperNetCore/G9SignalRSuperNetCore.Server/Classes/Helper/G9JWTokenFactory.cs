using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using G9SignalRSuperNetCore.Server.Enums;
using Microsoft.IdentityModel.Tokens;

namespace G9SignalRSuperNetCore.Server.Classes.Helper;

/// <summary>
///     Factory class for creating JSON Web Tokens (JWTs) and associated validation parameters.
/// </summary>
public class G9JWTokenFactory
{
    #region Fields And Properties

    /// <summary>
    ///     Specifies the status of rejection
    /// </summary>
    public readonly bool IsRejected;

    /// <summary>
    ///     The generated JWT as a string.
    /// </summary>
    public readonly string? JWToken;

    /// <summary>
    ///     Specifies the rejection reason
    /// </summary>
    public readonly string? RejectionReason;

    /// <summary>
    ///     The validation parameters used to validate the generated JWT.
    /// </summary>
    public readonly TokenValidationParameters? ValidationParameters;

    #endregion

    #region Methods

    /// <summary>
    ///     Initializes a new instance of the <see cref="G9JWTokenFactory" /> class.
    /// </summary>
    /// <param name="jwToken">The generated JWT token.</param>
    /// <param name="validationParameters">The token validation parameters.</param>
    private G9JWTokenFactory(string jwToken, TokenValidationParameters validationParameters)
    {
        JWToken = jwToken;
        ValidationParameters = validationParameters;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="G9JWTokenFactory" /> class for rejection.
    /// </summary>
    /// <param name="rejectionReason">Can set rejection reason message if needed, the client receive this message.</param>
    private G9JWTokenFactory(string? rejectionReason)
    {
        IsRejected = true;
        RejectionReason = rejectionReason;
        JWToken = null;
        ValidationParameters = null;
    }

    /// <summary>
    ///     Creates the security key and validation parameters for a JWT.
    /// </summary>
    /// <param name="jwtSecret">The secret key used to sign the token.</param>
    /// <param name="issuer">The issuer of the token.</param>
    /// <param name="audience">The audience of the token.</param>
    /// <param name="requireExpirationTime">Indicates whether the token requires an expiration time.</param>
    /// <param name="clockSkew">Optional clock skew for token expiration validation.</param>
    /// <param name="validateTokenReplay">Indicates whether token replay validation is enabled.</param>
    /// <param name="lifetimeValidator">Optional custom lifetime validator function.</param>
    /// <returns>A tuple containing the security key and token validation parameters.</returns>
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

    /// <summary>
    ///     Generates a JWT token with default claims.
    /// </summary>
    /// <param name="jwtSecret">The secret key used to sign the token.</param>
    /// <param name="issuer">The issuer of the token.</param>
    /// <param name="audience">The audience of the token.</param>
    /// <param name="expires">Optional expiration time for the token.</param>
    /// <param name="securityAlgorithm">The algorithm used to sign the token.</param>
    /// <returns>A <see cref="G9JWTokenFactory" /> instance containing the JWT and validation parameters.</returns>
    public static G9JWTokenFactory GenerateJWTToken(
        string jwtSecret, string issuer, string audience, DateTime? expires = null,
        G9ESecurityAlgorithms securityAlgorithm = G9ESecurityAlgorithms.HmacSha256)
    {
        var (key, validationParameters) =
            CreateKeyAndValidationParameters(jwtSecret, issuer, audience, expires != null);

        var credentials = new SigningCredentials(key, securityAlgorithm.ToSecurityAlgorithm());
        var token = new JwtSecurityToken(issuer, audience, expires: expires, signingCredentials: credentials);
        var jwt = new JwtSecurityTokenHandler().WriteToken(token);

        return new G9JWTokenFactory(jwt, validationParameters);
    }

    /// <summary>
    ///     Generates a JWT token with a username claim.
    /// </summary>
    /// <param name="jwtSecret">The secret key used to sign the token.</param>
    /// <param name="username">The username to include in the token claims.</param>
    /// <param name="issuer">The issuer of the token.</param>
    /// <param name="audience">The audience of the token.</param>
    /// <param name="expires">Optional expiration time for the token.</param>
    /// <param name="securityAlgorithm">The algorithm used to sign the token.</param>
    /// <returns>A <see cref="G9JWTokenFactory" /> instance containing the JWT and validation parameters.</returns>
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
    /// <param name="jwtSecret">The secret key used to sign the token.</param>
    /// <param name="username">The username to include in the token claims.</param>
    /// <param name="role">The role to include in the token claims.</param>
    /// <param name="issuer">The issuer of the token.</param>
    /// <param name="audience">The audience of the token.</param>
    /// <param name="expires">Optional expiration time for the token.</param>
    /// <param name="securityAlgorithm">The algorithm used to sign the token.</param>
    /// <returns>A <see cref="G9JWTokenFactory" /> instance containing the JWT and validation parameters.</returns>
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
    ///     Generates a JWT (JSON Web Token) with the specified secret, issuer, audience, claims, expiration time, and security
    ///     algorithm.
    /// </summary>
    /// <param name="jwtSecret">The secret key used for signing the JWT.</param>
    /// <param name="issuer">The issuer of the JWT (typically the application or service generating the token).</param>
    /// <param name="audience">The intended audience of the JWT (typically the recipient or service accepting the token).</param>
    /// <param name="claims">A list of claims to include in the JWT payload.</param>
    /// <param name="expires">An optional expiration time for the token. If not provided, the token does not expire.</param>
    /// <param name="securityAlgorithm">The security algorithm to use for signing the JWT (default is HMAC SHA-256).</param>
    /// <returns>A G9JWTokenFactory containing the generated JWT and associated validation parameters.</returns>
    public static G9JWTokenFactory GenerateJWTToken(
        string jwtSecret, string issuer, string audience, IList<Claim> claims, DateTime? expires = null,
        G9ESecurityAlgorithms securityAlgorithm = G9ESecurityAlgorithms.HmacSha256)
    {
        var (key, validationParameters) =
            CreateKeyAndValidationParameters(jwtSecret, issuer, audience, expires != null);

        var credentials = new SigningCredentials(key, securityAlgorithm.ToSecurityAlgorithm());
        var token = new JwtSecurityToken(issuer, audience, claims, expires: expires, signingCredentials: credentials);
        var jwt = new JwtSecurityTokenHandler().WriteToken(token);

        return new G9JWTokenFactory(jwt, validationParameters);
    }

    /// <summary>
    ///     Generates a JWT with the specified secret, issuer, audience, expiration time, clock skew, and security algorithm.
    /// </summary>
    /// <param name="jwtSecret">The secret key used for signing the JWT.</param>
    /// <param name="issuer">The issuer of the JWT.</param>
    /// <param name="audience">The intended audience of the JWT.</param>
    /// <param name="expires">An optional expiration time for the token.</param>
    /// <param name="clockSkew">An optional clock skew to allow for some variance in time between systems.</param>
    /// <param name="securityAlgorithm">The security algorithm to use for signing the JWT (default is HMAC SHA-256).</param>
    /// <returns>A G9JWTokenFactory containing the generated JWT and associated validation parameters.</returns>
    public static G9JWTokenFactory GenerateJWTToken(
        string jwtSecret,
        string issuer,
        string audience,
        DateTime? expires = null,
        TimeSpan? clockSkew = null,
        G9ESecurityAlgorithms securityAlgorithm = G9ESecurityAlgorithms.HmacSha256)
    {
        var (key, validationParameters) =
            CreateKeyAndValidationParameters(jwtSecret, issuer, audience, expires != null, clockSkew);

        var credentials = new SigningCredentials(key, securityAlgorithm.ToSecurityAlgorithm());
        var token = new JwtSecurityToken(issuer, audience, expires: expires, signingCredentials: credentials);
        var jwt = new JwtSecurityTokenHandler().WriteToken(token);

        return new G9JWTokenFactory(jwt, validationParameters);
    }

    /// <summary>
    ///     Generates a JWT with the specified secret, issuer, audience, expiration time, token replay validation, and security
    ///     algorithm.
    /// </summary>
    /// <param name="jwtSecret">The secret key used for signing the JWT.</param>
    /// <param name="issuer">The issuer of the JWT.</param>
    /// <param name="audience">The intended audience of the JWT.</param>
    /// <param name="expires">An optional expiration time for the token.</param>
    /// <param name="validateTokenReplay">A flag to indicate whether token replay validation should be enabled.</param>
    /// <param name="securityAlgorithm">The security algorithm to use for signing the JWT (default is HMAC SHA-256).</param>
    /// <returns>A G9JWTokenFactory containing the generated JWT and associated validation parameters.</returns>
    public static G9JWTokenFactory GenerateJWTToken(
        string jwtSecret,
        string issuer,
        string audience,
        DateTime? expires = null,
        bool validateTokenReplay = false,
        G9ESecurityAlgorithms securityAlgorithm = G9ESecurityAlgorithms.HmacSha256)
    {
        var (key, validationParameters) =
            CreateKeyAndValidationParameters(jwtSecret, issuer, audience, expires != null,
                validateTokenReplay: validateTokenReplay);

        var credentials = new SigningCredentials(key, securityAlgorithm.ToSecurityAlgorithm());
        var token = new JwtSecurityToken(issuer, audience, expires: expires, signingCredentials: credentials);
        var jwt = new JwtSecurityTokenHandler().WriteToken(token);

        return new G9JWTokenFactory(jwt, validationParameters);
    }

    /// <summary>
    ///     Generates a JWT with the specified secret, issuer, audience, expiration time, custom lifetime validator, and
    ///     security algorithm.
    /// </summary>
    /// <param name="jwtSecret">The secret key used for signing the JWT.</param>
    /// <param name="issuer">The issuer of the JWT.</param>
    /// <param name="audience">The intended audience of the JWT.</param>
    /// <param name="expires">An optional expiration time for the token.</param>
    /// <param name="lifetimeValidator">An optional custom lifetime validator to control the token's validity period.</param>
    /// <param name="securityAlgorithm">The security algorithm to use for signing the JWT (default is HMAC SHA-256).</param>
    /// <returns>A G9JWTokenFactory containing the generated JWT and associated validation parameters.</returns>
    public static G9JWTokenFactory GenerateJWTToken(
        string jwtSecret,
        string issuer,
        string audience,
        DateTime? expires = null,
        Func<DateTime?, DateTime?, SecurityToken, TokenValidationParameters, bool>? lifetimeValidator = null,
        G9ESecurityAlgorithms securityAlgorithm = G9ESecurityAlgorithms.HmacSha256)
    {
        var (key, validationParameters) =
            CreateKeyAndValidationParameters(jwtSecret, issuer, audience, expires != null,
                lifetimeValidator: lifetimeValidator);

        var credentials = new SigningCredentials(key, securityAlgorithm.ToSecurityAlgorithm());
        var token = new JwtSecurityToken(issuer, audience, expires: expires, signingCredentials: credentials);
        var jwt = new JwtSecurityTokenHandler().WriteToken(token);

        return new G9JWTokenFactory(jwt, validationParameters);
    }

    /// <summary>
    ///     Generates a JWT with the specified secret, issuer, audience, not-before time, expiration time, and security
    ///     algorithm.
    /// </summary>
    /// <param name="jwtSecret">The secret key used for signing the JWT.</param>
    /// <param name="issuer">The issuer of the JWT.</param>
    /// <param name="audience">The intended audience of the JWT.</param>
    /// <param name="notBefore">An optional not-before time for when the token becomes valid.</param>
    /// <param name="expires">An optional expiration time for the token.</param>
    /// <param name="securityAlgorithm">The security algorithm to use for signing the JWT (default is HMAC SHA-256).</param>
    /// <returns>A G9JWTokenFactory containing the generated JWT and associated validation parameters.</returns>
    public static G9JWTokenFactory GenerateJWTToken(
        string jwtSecret,
        string issuer,
        string audience,
        DateTime? notBefore,
        DateTime? expires = null,
        G9ESecurityAlgorithms securityAlgorithm = G9ESecurityAlgorithms.HmacSha256)
    {
        var (key, validationParameters) =
            CreateKeyAndValidationParameters(jwtSecret, issuer, audience, expires != null);

        var credentials = new SigningCredentials(key, securityAlgorithm.ToSecurityAlgorithm());
        var token = new JwtSecurityToken(issuer, audience, notBefore: notBefore, expires: expires,
            signingCredentials: credentials);
        var jwt = new JwtSecurityTokenHandler().WriteToken(token);

        return new G9JWTokenFactory(jwt, validationParameters);
    }

    /// <summary>
    ///     Generates a JWT with the specified secret, issuer, audience, claims, username, role, not-before time, expiration
    ///     time, clock skew, token replay validation, lifetime validator, and security algorithm.
    /// </summary>
    /// <param name="jwtSecret">The secret key used for signing the JWT.</param>
    /// <param name="issuer">The issuer of the JWT.</param>
    /// <param name="audience">The intended audience of the JWT.</param>
    /// <param name="claims">Optional list of claims to include in the JWT payload.</param>
    /// <param name="username">Optional username to be included as a claim in the JWT.</param>
    /// <param name="role">Optional role to be included as a claim in the JWT.</param>
    /// <param name="notBefore">Optional not-before time for when the token becomes valid.</param>
    /// <param name="expires">Optional expiration time for the token.</param>
    /// <param name="clockSkew">Optional clock skew to allow for some variance in time between systems.</param>
    /// <param name="validateTokenReplay">Flag to indicate whether token replay validation should be enabled.</param>
    /// <param name="lifetimeValidator">Optional custom lifetime validator to control the token's validity period.</param>
    /// <param name="securityAlgorithm">The security algorithm to use for signing the JWT (default is HMAC SHA-256).</param>
    /// <returns>A G9JWTokenFactory containing the generated JWT and associated validation parameters.</returns>
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
        // Create claims if username or role are provided
        var tokenClaims = claims?.ToList() ?? new List<Claim>();

        if (!string.IsNullOrEmpty(username))
        {
            tokenClaims.Add(new Claim(JwtRegisteredClaimNames.Sub, username));
            tokenClaims.Add(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()));
        }

        if (!string.IsNullOrEmpty(role)) tokenClaims.Add(new Claim(ClaimTypes.Role, role));

        // Generate validation parameters
        var (key, validationParameters) = CreateKeyAndValidationParameters(
            jwtSecret, issuer, audience, expires != null, clockSkew, validateTokenReplay, lifetimeValidator);

        // Create signing credentials
        var credentials = new SigningCredentials(key, securityAlgorithm.ToSecurityAlgorithm());

        // Create the token
        var token = new JwtSecurityToken(
            issuer,
            audience,
            tokenClaims.Count > 0 ? tokenClaims : null,
            notBefore,
            expires,
            credentials);

        // Write the token
        var jwt = new JwtSecurityTokenHandler().WriteToken(token);

        return new G9JWTokenFactory(jwt, validationParameters);
    }

    /// <summary>
    ///     Method to reject authorization
    /// </summary>
    /// <param name="rejectionReason">Can set rejection reason message if needed, the client receive this message.</param>
    /// <returns>An instance that specifies the rejection and its message.</returns>
    public static G9JWTokenFactory RejectAuthorize(string? rejectionReason)
    {
        return new G9JWTokenFactory(rejectionReason);
    }

    #endregion
}