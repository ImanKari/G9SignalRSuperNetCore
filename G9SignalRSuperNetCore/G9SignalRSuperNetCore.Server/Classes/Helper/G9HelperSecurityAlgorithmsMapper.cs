using G9SignalRSuperNetCore.Server.Enums;
using Microsoft.IdentityModel.Tokens;

namespace G9SignalRSuperNetCore.Server.Classes.Helper;

/// <summary>
///     Provides helper methods to map <see cref="G9ESecurityAlgorithms" /> to the corresponding
///     <see cref="SecurityAlgorithms" /> constants.
/// </summary>
public static class G9HelperSecurityAlgorithmsMapper
{
    /// <summary>
    ///     Maps a <see cref="G9ESecurityAlgorithms" /> value to the corresponding <see cref="SecurityAlgorithms" /> constant.
    /// </summary>
    /// <param name="algorithm">The <see cref="G9ESecurityAlgorithms" /> value to map.</param>
    /// <returns>The corresponding <see cref="SecurityAlgorithms" /> constant as a string.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the provided algorithm is not supported.</exception>
    public static string ToSecurityAlgorithm(this G9ESecurityAlgorithms algorithm)
    {
        return algorithm switch
        {
            G9ESecurityAlgorithms.HmacSha256 => SecurityAlgorithms.HmacSha256,
            G9ESecurityAlgorithms.HmacSha384 => SecurityAlgorithms.HmacSha384,
            G9ESecurityAlgorithms.HmacSha512 => SecurityAlgorithms.HmacSha512,
            G9ESecurityAlgorithms.RsaSha256 => SecurityAlgorithms.RsaSha256,
            G9ESecurityAlgorithms.RsaSha384 => SecurityAlgorithms.RsaSha384,
            G9ESecurityAlgorithms.RsaSha512 => SecurityAlgorithms.RsaSha512,
            G9ESecurityAlgorithms.RsaSsaPssSha256 => SecurityAlgorithms.RsaSsaPssSha256,
            G9ESecurityAlgorithms.RsaSsaPssSha384 => SecurityAlgorithms.RsaSsaPssSha384,
            G9ESecurityAlgorithms.RsaSsaPssSha512 => SecurityAlgorithms.RsaSsaPssSha512,
            G9ESecurityAlgorithms.EcdsaSha256 => SecurityAlgorithms.EcdsaSha256,
            G9ESecurityAlgorithms.EcdsaSha384 => SecurityAlgorithms.EcdsaSha384,
            G9ESecurityAlgorithms.EcdsaSha512 => SecurityAlgorithms.EcdsaSha512,
            G9ESecurityAlgorithms.None => SecurityAlgorithms.None,
            G9ESecurityAlgorithms.Aes128KW => SecurityAlgorithms.Aes128KW,
            G9ESecurityAlgorithms.Aes192KW => SecurityAlgorithms.Aes192KW,
            G9ESecurityAlgorithms.Aes256KW => SecurityAlgorithms.Aes256KW,
            G9ESecurityAlgorithms.RsaOAEP => SecurityAlgorithms.RsaOAEP,
            G9ESecurityAlgorithms.Rsa1_5 => SecurityAlgorithms.RsaPKCS1,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null)
        };
    }
}