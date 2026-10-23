using System.Security.Cryptography;
using System.Text;

namespace Intelibill.Application.Features.Users;

/// <summary>
/// Invitation secrets follow the same shape as the email-ownership challenges: 32 random bytes
/// sent to the invited address, only a SHA-256 hash stored, compared in fixed time.
/// </summary>
internal static class InvitationTokens
{
    public static string New() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static bool Matches(string storedHash, string presentedToken) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(storedHash),
            Encoding.ASCII.GetBytes(Hash(presentedToken)));
}
