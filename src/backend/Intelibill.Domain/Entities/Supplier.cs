using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class Supplier : BaseEntity
{
    public Guid ShopId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? ContactPersonName { get; private set; }
    public string? ContactPersonPhone { get; private set; }
    public string? Address { get; private set; }
    public string? City { get; private set; }
    public string? State { get; private set; }
    public string? Pin { get; private set; }
    public bool IsSystem { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsPreferred { get; private set; }

    private Supplier() { }

    public static Supplier Create(
        Guid shopId,
        string name,
        string? contactPersonName,
        string? contactPersonPhone,
        string? address,
        string? city,
        string? state,
        string? pin,
        bool isActive,
        bool isPreferred,
        bool isSystem = false)
    {
        return new Supplier
        {
            ShopId = shopId,
            Name = name.Trim(),
            ContactPersonName = NormalizeOptional(contactPersonName),
            ContactPersonPhone = NormalizeOptional(contactPersonPhone),
            Address = NormalizeOptional(address),
            City = NormalizeOptional(city),
            State = NormalizeOptional(state),
            Pin = NormalizeOptional(pin),
            IsSystem = isSystem,
            IsActive = isActive,
            IsPreferred = isPreferred,
        };
    }

    public static Supplier CreateUnknownSystemSupplier(Guid shopId)
    {
        return Create(
            shopId,
            "Unknown Supplier",
            null,
            null,
            null,
            null,
            null,
            null,
            isActive: true,
            isPreferred: false,
            isSystem: true);
    }

    public void Update(
        string name,
        string? contactPersonName,
        string? contactPersonPhone,
        string? address,
        string? city,
        string? state,
        string? pin,
        bool isActive,
        bool isPreferred)
    {
        Name = name.Trim();
        ContactPersonName = NormalizeOptional(contactPersonName);
        ContactPersonPhone = NormalizeOptional(contactPersonPhone);
        Address = NormalizeOptional(address);
        City = NormalizeOptional(city);
        State = NormalizeOptional(state);
        Pin = NormalizeOptional(pin);
        IsActive = isActive;
        IsPreferred = isPreferred;
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }
}
