namespace Intelibill.Licensing.Domain.Entities;

/// <summary>
/// A purpose-bound, one-use invitation for a provider administrator. Only the SHA-256 digest of
/// the emailed token is stored; the token itself exists only in memory and in the message.
/// </summary>
public sealed class AdminInvitation(
    Guid id, string email, string tokenDigest, Guid invitedBy, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(72);

    public Guid Id { get; private set; } = id;
    public string Email { get; private set; } = email;
    public string TokenDigest { get; private set; } = tokenDigest;
    public Guid InvitedBy { get; private set; } = invitedBy;
    public DateTimeOffset IssuedAt { get; private set; } = issuedAt;
    public DateTimeOffset ExpiresAt { get; private set; } = expiresAt;
    public DateTimeOffset? AcceptedAt { get; private set; }
    public Guid? AcceptedAdminId { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public int FailedAttempts { get; private set; }
    public uint Version { get; private set; }

    public bool IsOpen(DateTimeOffset now) => AcceptedAt is null && RevokedAt is null && ExpiresAt > now;

    /// <summary>Counts a wrong identity; the link retires once the budget is spent.</summary>
    public void RecordMismatch(DateTimeOffset now)
    {
        FailedAttempts++;
        if (FailedAttempts >= MaxFailedAttempts) RevokedAt = now;
    }

    public void Accept(Guid adminId, DateTimeOffset now)
    {
        AcceptedAdminId = adminId;
        AcceptedAt = now;
    }
}
