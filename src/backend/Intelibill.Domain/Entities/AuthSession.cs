using Intelibill.Domain.Common;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Entities;

/// <summary>
/// One signed-in device. The session id travels in the access token as <c>sid</c>, so every
/// protected request resolves back to a row that can be ended on its own: revoking one device
/// leaves the account's other devices working, which a <see cref="User.SecurityVersion"/> bump
/// cannot do.
/// </summary>
/// <remarks>
/// <see cref="SecurityVersion"/> and <see cref="RecoveryEpoch"/> are the proof this session was
/// started under. They are compared against the live account on every request, so a password
/// change or a database restore ends the session without needing to find and rewrite it.
/// </remarks>
public sealed class AuthSession : BaseEntity
{
    public Guid UserId { get; private set; }
    public long SecurityVersion { get; private set; }
    public string RecoveryEpoch { get; private set; } = string.Empty;
    public Guid? ActiveShopId { get; private set; }
    public long ScopeVersion { get; private set; }

    /// <summary>A description the owner can edit. Never read as authentication.</summary>
    public string? DeviceLabel { get; private set; }
    public string? UserAgent { get; private set; }
    public string? CreatedFromIp { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevocationReason { get; private set; }

    public User User { get; private set; } = null!;

    public bool IsRevoked => RevokedAt is not null;

    private AuthSession() { }

    public static AuthSession Start(
        Guid userId,
        long securityVersion,
        string recoveryEpoch,
        Guid? activeShopId,
        DeviceMetadata device,
        DateTimeOffset now,
        DateTimeOffset expiresAt) =>
        new()
        {
            UserId = userId,
            SecurityVersion = securityVersion,
            RecoveryEpoch = recoveryEpoch,
            ActiveShopId = activeShopId,
            ScopeVersion = 0,
            UserAgent = device.UserAgent,
            CreatedFromIp = device.IpAddress,
            LastSeenAt = now,
            ExpiresAt = expiresAt,
        };

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    /// <summary>
    /// Records which shop this device is working in, so its owner can recognise it in a session
    /// list. The scope is description, not authority: what a token may do in a shop is decided by
    /// the live membership behind it, which is why re-selecting the same shop changes nothing and
    /// why moving does not reach back and invalidate tokens the client already holds.
    /// </summary>
    public void ChangeScope(Guid? activeShopId, DateTimeOffset now)
    {
        Touch(now);

        if (ActiveShopId == activeShopId)
            return;

        ActiveShopId = activeShopId;
        ScopeVersion++;
    }

    public void Touch(DateTimeOffset now)
    {
        if (now > LastSeenAt)
            LastSeenAt = now;
    }

    /// <summary>
    /// Ends the session. The first reason recorded is kept: a session revoked by its owner and then
    /// caught by a password change was already gone, and the owner's reason is the true one.
    /// </summary>
    public void Revoke(string reason, DateTimeOffset now)
    {
        if (RevokedAt is not null)
            return;

        RevokedAt = now;
        RevocationReason = reason;
    }

    public void Relabel(string? label) =>
        DeviceLabel = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
}
