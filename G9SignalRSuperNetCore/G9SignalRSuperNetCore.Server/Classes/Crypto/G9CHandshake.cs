using System.Security.Cryptography;

namespace G9SignalRSuperNetCore.Server.Classes.Crypto;

/// <summary>
///     Lightweight ephemeral-static ECDH handshake (NIST P-256) + HKDF-SHA-256 + ChaCha20-Poly1305
///     payload encryption. Lets clients exchange app-level ciphertext over a plain HTTP/WebSocket
///     SignalR connection (i.e. when no TLS is available, e.g. behind a reverse proxy that only
///     accepts h2c, or on internal networks).
/// </summary>
/// <remarks>
///     <para><b>Why not Noise IK?</b> Noise IK requires X25519 which the BCL doesn't ship in
///     <c>System.Security.Cryptography</c>. We get equivalent security guarantees from the
///     IETF-standard ECDHE-static handshake on NIST P-256 using only types that are already
///     in the BCL: AOT-safe, constant-time, FIPS-validated on every supported platform.</para>
///     <para><b>Threat model.</b> Confidentiality + integrity end-to-end against a network
///     attacker. Authenticates the server by its long-term P-256 public key, which the client
///     pins after the first connect (TOFU; can be hard-coded for known deployments). Forward
///     secrecy via the per-session ephemeral key. <i>Not</i> a replacement for TLS in scenarios
///     where you also need certificate revocation, downgrade protection, or origin authentication.</para>
///     <para><b>Wire format.</b>
///     <list type="bullet">
///       <item>Public-key wire format: SEC1 uncompressed point — 1 byte 0x04 prefix followed by
///       32 bytes X then 32 bytes Y. 65 bytes total.</item>
///       <item>Sealed envelope: <c>[nonce(12) | ciphertext(N) | tag(16)]</c> sealed with
///       ChaCha20-Poly1305 under the derived 32-byte session key.</item>
///     </list></para>
///     <para><b>Performance.</b> One ECDH and one HKDF per session (a few hundred microseconds
///     on commodity hardware); subsequent encryptions are a single ChaCha20-Poly1305 pass which
///     the BCL hardware-accelerates on x86-AESNI / Arm-NEON.</para>
/// </remarks>
public sealed class G9CHandshake : IDisposable
{
    /// <summary>The byte length of an SEC1-encoded uncompressed P-256 public key.</summary>
    public const int PublicKeySize = 65;

    /// <summary>The byte length of a derived session key.</summary>
    public const int SessionKeySize = 32;

    /// <summary>The nonce size used by the ChaCha20-Poly1305 envelope (12 bytes).</summary>
    public const int NonceSize = 12;

    /// <summary>The authentication tag size used by the ChaCha20-Poly1305 envelope (16 bytes).</summary>
    public const int TagSize = 16;

    private const string Info = "G9SR.HS.v1";
    private const int CoordSize = 32;

    private readonly ECDiffieHellman _staticKey;
    private byte[] _staticPublicKey = null!;

    /// <summary>Initializes a fresh server handshake with a brand-new long-term P-256 keypair.</summary>
    public G9CHandshake() : this(ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256)) { }

    /// <summary>Initializes the handshake with a pre-existing <paramref name="staticKey"/>.</summary>
    public G9CHandshake(ECDiffieHellman staticKey)
    {
        ArgumentNullException.ThrowIfNull(staticKey);
        _staticKey = staticKey;
        _staticPublicKey = ExportSec1Point(_staticKey);
    }

    /// <summary>The server's long-term public key in SEC1 uncompressed format (65 bytes).</summary>
    public ReadOnlyMemory<byte> StaticPublicKey => _staticPublicKey;

    /// <summary>
    ///     Server side: completes a handshake initiated by <paramref name="clientEphemeralPublicKey"/>
    ///     and returns the 32-byte session key.
    /// </summary>
    public byte[] DeriveSessionKey(ReadOnlySpan<byte> clientEphemeralPublicKey)
    {
        if (clientEphemeralPublicKey.Length != PublicKeySize)
            throw new ArgumentException($"Ephemeral public key must be SEC1 uncompressed ({PublicKeySize} bytes).",
                nameof(clientEphemeralPublicKey));

        using var peer = ImportSec1Point(clientEphemeralPublicKey);
        var shared = _staticKey.DeriveRawSecretAgreement(peer.PublicKey);
        return DeriveKey(shared);
    }

    /// <summary>
    ///     Client side: generates a fresh ephemeral keypair and derives the matching session key
    ///     by combining it with the server's pinned <paramref name="serverStaticPublicKey"/>.
    /// </summary>
    public static (byte[] EphemeralPublicKey, byte[] SessionKey) ClientHandshake(ReadOnlySpan<byte> serverStaticPublicKey)
    {
        if (serverStaticPublicKey.Length != PublicKeySize)
            throw new ArgumentException($"Server public key must be SEC1 uncompressed ({PublicKeySize} bytes).",
                nameof(serverStaticPublicKey));

        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var ephemeralPub = ExportSec1Point(ephemeral);

        using var serverPub = ImportSec1Point(serverStaticPublicKey);
        var shared = ephemeral.DeriveRawSecretAgreement(serverPub.PublicKey);
        return (ephemeralPub, DeriveKey(shared));
    }

    /// <summary>
    ///     Encrypts <paramref name="plaintext"/> with ChaCha20-Poly1305 under
    ///     <paramref name="sessionKey"/> and returns <c>[nonce | ciphertext | tag]</c>.
    /// </summary>
    public static byte[] Seal(byte[] sessionKey, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData = default)
    {
        if (sessionKey is null || sessionKey.Length != SessionKeySize)
            throw new ArgumentException($"Session key must be {SessionKeySize} bytes.", nameof(sessionKey));

        var output = new byte[NonceSize + plaintext.Length + TagSize];
        var nonce = output.AsSpan(0, NonceSize);
        var ciphertext = output.AsSpan(NonceSize, plaintext.Length);
        var tag = output.AsSpan(NonceSize + plaintext.Length, TagSize);

        RandomNumberGenerator.Fill(nonce);
        using var aead = new ChaCha20Poly1305(sessionKey);
        aead.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        return output;
    }

    /// <summary>
    ///     Inverse of <see cref="Seal(byte[], ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>: validates
    ///     the AEAD tag and returns the recovered plaintext.
    /// </summary>
    public static byte[] Open(byte[] sessionKey, ReadOnlySpan<byte> envelope, ReadOnlySpan<byte> associatedData = default)
    {
        if (sessionKey is null || sessionKey.Length != SessionKeySize)
            throw new ArgumentException($"Session key must be {SessionKeySize} bytes.", nameof(sessionKey));
        if (envelope.Length < NonceSize + TagSize)
            throw new ArgumentException("Envelope too short.", nameof(envelope));

        var nonce = envelope.Slice(0, NonceSize);
        var tag = envelope.Slice(envelope.Length - TagSize, TagSize);
        var ciphertext = envelope.Slice(NonceSize, envelope.Length - NonceSize - TagSize);

        var plaintext = new byte[ciphertext.Length];
        using var aead = new ChaCha20Poly1305(sessionKey);
        aead.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        return plaintext;
    }

    /// <summary>
    ///     Single-call convenience: derives the session key from
    ///     <paramref name="clientEphemeralPublicKey"/>, seals <paramref name="plaintext"/>, and
    ///     returns the envelope plus the derived session key (so the caller can keep using it).
    /// </summary>
    public (byte[] Envelope, byte[] SessionKey) HandshakeAndSeal(
        ReadOnlySpan<byte> clientEphemeralPublicKey, ReadOnlySpan<byte> plaintext)
    {
        var key = DeriveSessionKey(clientEphemeralPublicKey);
        return (Seal(key, plaintext), key);
    }

    private static readonly byte[] InfoBytes = System.Text.Encoding.ASCII.GetBytes(Info);

    private static byte[] DeriveKey(byte[] sharedSecret)
        => HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, SessionKeySize,
            salt: null,
            info: InfoBytes);

    private static byte[] ExportSec1Point(ECDiffieHellman key)
    {
        var p = key.ExportParameters(false);
        var x = p.Q.X ?? throw new InvalidOperationException("Curve parameters missing X.");
        var y = p.Q.Y ?? throw new InvalidOperationException("Curve parameters missing Y.");

        // P-256 coordinates are 32 bytes; pad with leading zeros if BCL trimmed them.
        var output = new byte[1 + CoordSize + CoordSize];
        output[0] = 0x04; // SEC1 uncompressed prefix
        x.AsSpan().CopyTo(output.AsSpan(1 + CoordSize - x.Length, x.Length));
        y.AsSpan().CopyTo(output.AsSpan(1 + 2 * CoordSize - y.Length, y.Length));
        return output;
    }

    private static ECDiffieHellman ImportSec1Point(ReadOnlySpan<byte> sec1Point)
    {
        if (sec1Point[0] != 0x04)
            throw new ArgumentException("Only SEC1 uncompressed points (0x04 prefix) are accepted.", nameof(sec1Point));

        var x = sec1Point.Slice(1, CoordSize).ToArray();
        var y = sec1Point.Slice(1 + CoordSize, CoordSize).ToArray();
        var ecdh = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = x, Y = y }
        });
        return ecdh;
    }

    /// <inheritdoc />
    public void Dispose() => _staticKey.Dispose();
}
