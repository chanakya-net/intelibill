using Intelibill.Domain.Common;
namespace Intelibill.Domain.Entities;

public sealed class IdentityMail : BaseEntity
{
    public Guid? ChallengeId { get; private set; }
    public string Destination { get; private set; } = string.Empty;
    public string EncryptedContent { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    private IdentityMail() { }
    public static IdentityMail Create(Guid? challengeId, string destination, string encryptedContent, DateTimeOffset expiresAt) => new()
    { ChallengeId = challengeId, Destination = destination, EncryptedContent = encryptedContent, ExpiresAt = expiresAt, NextAttemptAt = DateTimeOffset.UtcNow };
    public void Complete(DateTimeOffset now) { CompletedAt = now; EncryptedContent = string.Empty; }
    public void Failed(DateTimeOffset now) { Attempts++; NextAttemptAt = now.AddSeconds(Math.Min(900, 30 * Math.Pow(2, Attempts))); }
}
