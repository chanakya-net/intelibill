namespace Intelibill.Licensing.Domain.Entities;

public sealed class AdminSession(Guid id, Guid adminId, DateTimeOffset mfaVerifiedAt)
{
    public static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(12);

    public Guid Id { get; private set; } = id;
    public Guid AdminId { get; private set; } = adminId;
    public DateTimeOffset CreatedAt { get; private set; } = mfaVerifiedAt;
    public DateTimeOffset MfaVerifiedAt { get; private set; } = mfaVerifiedAt;
    public DateTimeOffset LastSeenAt { get; private set; } = mfaVerifiedAt;
    public DateTimeOffset? RevokedAt { get; private set; }
}
