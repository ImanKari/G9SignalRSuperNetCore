namespace G9SignalRSuperNetCore.Server.Enums;

/// <summary>
///     Represents the supported security algorithms for JWT (JSON Web Token) operations.
/// </summary>
public enum G9ESecurityAlgorithms
{
    /// <summary>
    ///     HMAC using SHA-256 for signing.
    /// </summary>
    HmacSha256,

    /// <summary>
    ///     HMAC using SHA-384 for signing.
    /// </summary>
    HmacSha384,

    /// <summary>
    ///     HMAC using SHA-512 for signing.
    /// </summary>
    HmacSha512,

    /// <summary>
    ///     RSA using SHA-256 for signing.
    /// </summary>
    RsaSha256,

    /// <summary>
    ///     RSA using SHA-384 for signing.
    /// </summary>
    RsaSha384,

    /// <summary>
    ///     RSA using SHA-512 for signing.
    /// </summary>
    RsaSha512,

    /// <summary>
    ///     RSASSA-PSS using SHA-256 for signing with probabilistic signature scheme.
    /// </summary>
    RsaSsaPssSha256,

    /// <summary>
    ///     RSASSA-PSS using SHA-384 for signing with probabilistic signature scheme.
    /// </summary>
    RsaSsaPssSha384,

    /// <summary>
    ///     RSASSA-PSS using SHA-512 for signing with probabilistic signature scheme.
    /// </summary>
    RsaSsaPssSha512,

    /// <summary>
    ///     ECDSA using P-256 curve with SHA-256 for signing.
    /// </summary>
    EcdsaSha256,

    /// <summary>
    ///     ECDSA using P-384 curve with SHA-384 for signing.
    /// </summary>
    EcdsaSha384,

    /// <summary>
    ///     ECDSA using P-521 curve with SHA-512 for signing.
    /// </summary>
    EcdsaSha512,

    /// <summary>
    ///     No signing algorithm. Indicates plaintext usage.
    /// </summary>
    None,

    /// <summary>
    ///     AES Key Wrap with 128-bit keys for encryption.
    /// </summary>
    Aes128KW,

    /// <summary>
    ///     AES Key Wrap with 192-bit keys for encryption.
    /// </summary>
    Aes192KW,

    /// <summary>
    ///     AES Key Wrap with 256-bit keys for encryption.
    /// </summary>
    Aes256KW,

    /// <summary>
    ///     RSAES-OAEP using SHA-256 for encryption.
    /// </summary>
    RsaOAEP,

    /// <summary>
    ///     RSAES-PKCS1-v1_5 for encryption.
    /// </summary>
    Rsa1_5
}