using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Intelibill.Application.Common.Security;

/// <summary>
/// The refresh credential's secret and the only form of it that is stored. 64 random bytes are
/// handed to the client as Base64Url; the database keeps the SHA-256 of that string, hex-encoded the
/// same way as invitation tokens. The secret carries 512 bits of entropy, so a fast hash is enough:
/// there is nothing to brute-force, only a stored value that must not be replayable.
/// </summary>
public static class RefreshTokenSecrets
{
    private const int SecretBytes = 64;

    public static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretBytes));

    public static string Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
