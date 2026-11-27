using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

/// <summary>
/// One purpose-bound proof that somebody can receive a message at a phone number. The code itself
/// is never stored: <see cref="CodeHash"/> holds a keyed HMAC of it, because a plain digest over a
/// six-digit space is recoverable from a database dump in milliseconds. <see cref="KeyId"/> names
/// the key that produced it so the key can be rotated without invalidating rows that are still
/// open.
/// </summary>
public sealed class PhoneChallenge : BaseEntity
{
    public string Purpose { get; private set; } = string.Empty;

    /// <summary>The destination in E.164 form. Normalised before the row is created.</summary>
    public string Destination { get; private set; } = string.Empty;

    public string CodeHash { get; private set; } = string.Empty;

    public string KeyId { get; private set; } = string.Empty;

    /// <summary>The account this challenge acts on, or null while enrolling a new one.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>
    /// The account version the challenge was issued against. Session creation refuses a proof
    /// whose version has moved, so a code issued before a password reset cannot be presented after
    /// one as current authority.
    /// </summary>
    public long SecurityVersion { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>The earliest moment a replacement code may be requested for this destination.</summary>
    public DateTimeOffset ResendAllowedAt { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    private PhoneChallenge() { }

    public static PhoneChallenge Create(
        string purpose,
        string destination,
        string codeHash,
        string keyId,
        DateTimeOffset expiresAt,
        DateTimeOffset resendAllowedAt,
        Guid? userId = null,
        long version = 0) => new()
        {
            Purpose = purpose,
            Destination = destination,
            CodeHash = codeHash,
            KeyId = keyId,
            ExpiresAt = expiresAt,
            ResendAllowedAt = resendAllowedAt,
            UserId = userId,
            SecurityVersion = version,
        };

    /// <summary>
    /// Open means a code presented now could still succeed: not consumed, not expired, and inside
    /// the attempt budget. Expiry is exclusive, so a code presented at the expiry instant fails.
    /// </summary>
    public bool IsOpen(DateTimeOffset now, int maxAttempts) =>
        ConsumedAt is null && ExpiresAt > now && Attempts < maxAttempts;

    /// <summary>
    /// Records a wrong guess and reports whether the budget is now spent. The caller holds the row
    /// lock, so the count cannot be shared between two concurrent guesses.
    /// </summary>
    public bool RegisterFailedAttempt(int maxAttempts)
    {
        Attempts++;
        return Attempts >= maxAttempts;
    }

    /// <summary>
    /// Spends the proof. Single use: a consumed challenge is never open again, which is what stops
    /// one delivered code from producing two sessions.
    /// </summary>
    public void Consume(DateTimeOffset now) => ConsumedAt = now;
}
