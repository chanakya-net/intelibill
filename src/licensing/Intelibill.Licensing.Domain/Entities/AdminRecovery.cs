namespace Intelibill.Licensing.Domain.Entities;

public enum AdminRecoveryKind
{
    RecoveryCode = 1,
    ReviewedReset = 2,
}

public enum AdminRecoveryReason
{
    AuthenticatorLost = 1,
    RecoveryCodesLost = 2,
    SuspectedCompromise = 3,
}

public enum AdminRecoveryVerification
{
    VideoCall = 1,
    InPerson = 2,
    VerifiedCallback = 3,
}

/// <summary>The reviewer's explicit, non-secret evidence for a reset. No free text beyond a bounded reference.</summary>
public sealed record AdminRecoveryEvidence(
    AdminRecoveryReason Reason, AdminRecoveryVerification Verification, string Reference);

/// <summary>
/// One recovery of an administrator's MFA. A recovery code is redeemed at once; a reviewed reset
/// carries the reviewer's evidence and a one-use grant whose SHA-256 digest alone is stored.
/// Either way the administrator must enroll a new authenticator before any business session.
/// </summary>
public sealed class AdminRecovery
{
    public static readonly TimeSpan GrantLifetime = TimeSpan.FromHours(24);

    private AdminRecovery(Guid id, Guid adminId, AdminRecoveryKind kind, DateTimeOffset startedAt)
    {
        Id = id;
        AdminId = adminId;
        Kind = kind;
        StartedAt = startedAt;
    }

    public Guid Id { get; private set; }
    public Guid AdminId { get; private set; }
    public AdminRecoveryKind Kind { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public Guid? ReviewerId { get; private set; }
    public AdminRecoveryReason? Reason { get; private set; }
    public AdminRecoveryVerification? Verification { get; private set; }
    public string? EvidenceReference { get; private set; }
    public string? GrantDigest { get; private set; }
    public DateTimeOffset? GrantExpiresAt { get; private set; }
    public DateTimeOffset? RedeemedAt { get; private set; }
    public DateTimeOffset? RetiredAt { get; private set; }
    public uint Version { get; private set; }

    public static AdminRecovery FromCode(Guid adminId, DateTimeOffset now) =>
        new(Guid.NewGuid(), adminId, AdminRecoveryKind.RecoveryCode, now) { RedeemedAt = now };

    public static AdminRecovery Reviewed(Guid adminId, Guid reviewerId, AdminRecoveryEvidence evidence,
        string grantDigest, DateTimeOffset now) =>
        new(Guid.NewGuid(), adminId, AdminRecoveryKind.ReviewedReset, now)
        {
            ReviewerId = reviewerId,
            Reason = evidence.Reason,
            Verification = evidence.Verification,
            EvidenceReference = evidence.Reference,
            GrantDigest = grantDigest,
            GrantExpiresAt = now + GrantLifetime,
        };

    public bool IsOpen(DateTimeOffset now) =>
        Kind == AdminRecoveryKind.ReviewedReset && RedeemedAt is null && RetiredAt is null && GrantExpiresAt > now;

    public void Redeem(DateTimeOffset now) => RedeemedAt ??= now;
}
