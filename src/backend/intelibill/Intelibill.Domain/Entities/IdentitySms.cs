using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

/// <summary>
/// One outbound identity SMS, held encrypted until a worker delivers it. The verification code
/// exists in this row and nowhere else — not in an API response, not in a log line — so reading it
/// back requires the outbox key.
/// </summary>
public sealed class IdentitySms : BaseEntity
{
    /// <summary>The challenge this message carries a code for, or null for a security notice.</summary>
    public Guid? ChallengeId { get; private set; }

    public string Destination { get; private set; } = string.Empty;

    public string EncryptedContent { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    private IdentitySms() { }

    public static IdentitySms Create(Guid? challengeId, string destination, string encryptedContent, DateTimeOffset expiresAt) => new()
    {
        ChallengeId = challengeId,
        Destination = destination,
        EncryptedContent = encryptedContent,
        ExpiresAt = expiresAt,
        NextAttemptAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Delivered, superseded or expired. The body is dropped either way.</summary>
    public void Complete(DateTimeOffset now)
    {
        CompletedAt = now;
        EncryptedContent = string.Empty;
    }

    public void Failed(DateTimeOffset now)
    {
        Attempts++;
        NextAttemptAt = now.AddSeconds(Math.Min(900, 30 * Math.Pow(2, Attempts)));
    }
}
