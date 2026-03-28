using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class Shop : BaseEntity
{
    public string Name { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string State { get; private set; } = string.Empty;
    public string Pincode { get; private set; } = string.Empty;
    public string? ContactPerson { get; private set; }
    public string? MobileNumber { get; private set; }

    private readonly List<ShopMembership> _memberships = [];
    public IReadOnlyList<ShopMembership> Memberships => _memberships.AsReadOnly();

    private Shop() { }

    public static Shop Create(
        string name,
        string address,
        string city,
        string state,
        string pincode,
        string? contactPerson,
        string? mobileNumber)
    {
        return new Shop
        {
            Name = name.Trim(),
            Address = address.Trim(),
            City = city.Trim(),
            State = state.Trim(),
            Pincode = pincode.Trim(),
            ContactPerson = NormalizeOptional(contactPerson),
            MobileNumber = NormalizeOptional(mobileNumber),
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

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }
}