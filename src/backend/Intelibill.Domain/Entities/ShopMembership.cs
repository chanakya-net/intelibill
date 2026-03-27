using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class ShopMembership : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid UserId { get; private set; }
    public ShopRole Role { get; private set; }
    public bool IsDefault { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }

    public Shop Shop { get; private set; } = null!;
    public User User { get; private set; } = null!;

    private ShopMembership() { }

    public static ShopMembership Create(Guid shopId, Guid userId, ShopRole role, bool isDefault)
    {
        return new ShopMembership
        {
            ShopId = shopId,
            UserId = userId,
            Role = role,
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

    internal void AttachShop(Shop shop)
    {
        Shop = shop;
    }

    internal void AttachUser(User user)
    {
        User = user;
    }
}