using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

/// <summary>
/// A single-use offer of a shop-scoped role to one verified email address. An invitation grants
/// nothing: only acceptance by the account that owns that verified address creates or reinstates a
/// membership, and only while the inviter still holds ownership of the shop.
/// </summary>
public sealed class ShopInvitation : BaseEntity
{
    public Guid ShopId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public ShopRole Role { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public Guid InvitedByUserId { get; private set; }

    /// <summary>
    /// The inviter's membership authority version at issue time. An invitation issued under a role
    /// or ownership that has since changed is treated as void rather than honoured.
    /// </summary>
    public long InviterAuthorityVersion { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? AcceptedByUserId { get; private set; }

    private ShopInvitation() { }

    public static ShopInvitation Create(
        Guid shopId,
        string email,
        ShopRole role,
        string tokenHash,
        Guid invitedByUserId,
        long inviterAuthorityVersion,
        DateTimeOffset expiresAt) => new()
        {
            ShopId = shopId,
            Email = email.Trim().ToLowerInvariant(),
            Role = role,
            TokenHash = tokenHash,
            InvitedByUserId = invitedByUserId,
            InviterAuthorityVersion = inviterAuthorityVersion,
            ExpiresAt = expiresAt,
        };

    public bool IsPending(DateTimeOffset now) =>
        AcceptedAt is null && RevokedAt is null && ExpiresAt > now;

    public void Accept(Guid userId, DateTimeOffset now)
    {
        AcceptedAt = now;
        AcceptedByUserId = userId;
        UpdatedAt = now;
    }

    public void Revoke(DateTimeOffset now)
    {
        RevokedAt = now;
        UpdatedAt = now;
    }

    public string DescribeStatus(DateTimeOffset now) =>
        AcceptedAt is not null ? "Accepted"
        : RevokedAt is not null ? "Revoked"
        : ExpiresAt <= now ? "Expired"
        : "Pending";
}
