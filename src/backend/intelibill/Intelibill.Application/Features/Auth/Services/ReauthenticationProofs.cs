using System.Security.Cryptography;
using System.Text;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Auth.Services;

/// <summary>
/// Spends a reauthentication proof — the short-lived single-use value a successful password check
/// hands back. Shared by the email-change and phone-change flows so "a password was checked a
/// moment ago, by this account, at this version" has one definition rather than one per flow.
/// <para>
/// Must be called inside a transaction that already holds the identity advisory lock: the proof is
/// consumed in the same transaction as the change it authorises, so a proof cannot be spent twice
/// by two requests racing.
/// </para>
/// </summary>
public sealed class ReauthenticationProofs(IEmailOwnershipRepository repository, TimeProvider clock)
{
    internal const string Purpose = "reauthentication";

    public async Task<bool> ConsumeAsync(Guid actor, long version, string? proof, CancellationToken ct)
    {
        var parts = (proof ?? string.Empty).Split('.', 2);
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var id))
            return false;

        var token = parts[1];
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
            return false;

        var challenge = await repository.FindAsync(id, ct);
        if (challenge is null ||
            !string.Equals(challenge.Purpose, Purpose, StringComparison.Ordinal) ||
            challenge.ConsumedAt is not null ||
            challenge.ExpiresAt <= clock.GetUtcNow() ||
            challenge.UserId != actor ||
            challenge.SecurityVersion != version ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(challenge.TokenHash),
                Encoding.ASCII.GetBytes(Hash(token))))
            return false;

        challenge.Consume(clock.GetUtcNow());
        return true;
    }

    internal static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
