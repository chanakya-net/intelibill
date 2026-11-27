using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class ShopMembership : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid UserId { get; private set; }
    public ShopRole Role { get; private set; }
    public MembershipStatus Status { get; private set; } = MembershipStatus.Active;
    public bool IsDefault { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <summary>
    /// Incremented on every authority change (role or status). Callers send the version they read
    /// so a stale administration screen cannot overwrite a newer decision, and grants issued under
    /// an older version can be recognised as void.
    /// </summary>
    public long AuthorityVersion { get; private set; }

    public Shop Shop { get; private set; } = null!;
    public User User { get; private set; } = null!;

    public bool IsActive => Status == MembershipStatus.Active;

    private ShopMembership() { }

    public static ShopMembership Create(Guid shopId, Guid userId, ShopRole role, bool isDefault)
    {
        return new ShopMembership
        {
            ShopId = shopId,
            UserId = userId,
            Role = role,
            Status = MembershipStatus.Active,
            IsDefault = isDefault,
        };
    }

    public void SetDefault(bool isDefault)
    {
        IsDefault = isDefault;
    }

    public void MarkUsed()
    {
        LastUsedAt = DateTimeOffset.UtcNow;
    }

    public void SetRole(ShopRole role)
    {
        Role = role;
    }

    /// <summary>
    /// Applies a shop-scoped authority decision. A membership that is no longer active also stops
    /// being the account's default shop, so the next sign-in does not land in a shop it cannot use.
    /// </summary>
    public void ChangeAuthority(ShopRole role, MembershipStatus status)
    {
        Role = role;
        Status = status;
        AuthorityVersion++;
        UpdatedAt = DateTimeOffset.UtcNow;

        if (status != MembershipStatus.Active)
        {
            IsDefault = false;
        }
    }

    /// <summary>
    /// Re-admits a previously suspended or removed member through a fresh accepted invitation.
    /// </summary>
    public void Reinstate(ShopRole role) => ChangeAuthority(role, MembershipStatus.Active);

    internal void AttachShop(Shop shop)
    {
        Shop = shop;
    }

    internal void AttachUser(User user)
    {
        User = user;
    }
}
