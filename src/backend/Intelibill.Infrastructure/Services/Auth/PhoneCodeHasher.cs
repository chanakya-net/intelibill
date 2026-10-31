using System.Security.Cryptography;
using System.Text;
using Intelibill.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Intelibill.Infrastructure.Services.Auth;

/// <summary>
/// Keyed HMAC over the verification code, with the key held in the configured secret store under
/// <c>Identity:PhoneCodeKey</c> as a base64 32-byte value.
/// <para>
/// The key id is derived from the key itself, so rotating the secret changes the id and challenges
/// hashed with the old key stop matching rather than silently comparing against the wrong key.
/// Those challenges live five minutes; the holder requests a new code.
/// </para>
/// </summary>
internal sealed class PhoneCodeHasher(IConfiguration configuration) : IPhoneCodeHasher
{
    public bool Available => TryKey(out _);

    public string KeyId => TryKey(out var key) ? DeriveKeyId(key) : string.Empty;

    public string Hash(string code)
    {
        if (!TryKey(out var key))
            throw new InvalidOperationException("Identity:PhoneCodeKey must contain a base64-encoded 32-byte key.");

        return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(code)));
    }

    public bool Matches(string code, string codeHash, string keyId)
    {
        if (!TryKey(out var key) || !string.Equals(keyId, DeriveKeyId(key), StringComparison.Ordinal))
            return false;

        var candidate = Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(code)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(candidate),
            Encoding.ASCII.GetBytes(codeHash));
    }

    private static string DeriveKeyId(byte[] key) => Convert.ToHexString(SHA256.HashData(key))[..8];

    private bool TryKey(out byte[] key)
    {
        try
        {
            key = Convert.FromBase64String(configuration["Identity:PhoneCodeKey"] ?? string.Empty);
            return key.Length == 32;
        }
        catch (FormatException)
        {
            key = [];
            return false;
        }
    }
}
