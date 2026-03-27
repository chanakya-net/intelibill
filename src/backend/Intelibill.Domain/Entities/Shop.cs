using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class Shop : BaseEntity
{
    public string Name { get; private set; } = string.Empty;

    private readonly List<ShopMembership> _memberships = [];
    public IReadOnlyList<ShopMembership> Memberships => _memberships.AsReadOnly();

    private Shop() { }

    public static Shop Create(string name)
    {
        return new Shop
        {
            Name = name.Trim(),
        };
    }

    public void Rename(string name)
    {
        Name = name.Trim();
    }

    public void AddMembership(ShopMembership membership)
    {
        membership.AttachShop(this);
        _memberships.Add(membership);
    }
}