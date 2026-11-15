namespace Intelibill.Licensing.Domain.Entities;

public sealed class AdminSession(Guid id, Guid adminId, DateTimeOffset mfaVerifiedAt)
{
    public static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(12);

    public Guid Id { get; private set; } = id;
    public uint Version { get; private set; }
    public Guid AdminId { get; private set; } = adminId;
    public DateTimeOffset CreatedAt { get; private set; } = mfaVerifiedAt;
    public DateTimeOffset MfaVerifiedAt { get; private set; } = mfaVerifiedAt;
    public DateTimeOffset LastSeenAt { get; private set; } = mfaVerifiedAt;
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>The instant the session ends however often it is renewed.</summary>
    public DateTimeOffset AbsoluteEnd => CreatedAt + AbsoluteLifetime;

    /// <summary>
    /// Live at <paramref name="now"/>: not revoked, inside both the idle and absolute windows.
    /// Renewal never moves <see cref="MfaVerifiedAt"/> or <see cref="CreatedAt"/>.
    /// </summary>
    public bool IsLiveAt(DateTimeOffset now) =>
        RevokedAt is null && MfaVerifiedAt <= now && now < AbsoluteEnd && now < LastSeenAt + IdleLifetime;

    public void Touch(DateTimeOffset now)
    {
        if (now > LastSeenAt) LastSeenAt = now;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now < CreatedAt ? CreatedAt : now;
}
