using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class Supplier : BaseEntity
{
    public Guid OwnerUserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? ContactPersonName { get; private set; }
    public string? ContactPersonPhone { get; private set; }
    public string Address { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string State { get; private set; } = string.Empty;
    public string Pin { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public bool IsPreferred { get; private set; }

    private Supplier() { }

    public static Supplier Create(
        Guid ownerUserId,
        string name,
        string? contactPersonName,
        string? contactPersonPhone,
        string address,
        string city,
        string state,
        string pin,
        bool isActive,
        bool isPreferred)
    {
        return new Supplier
        {
            OwnerUserId = ownerUserId,
            Name = name.Trim(),
            ContactPersonName = NormalizeOptional(contactPersonName),
            ContactPersonPhone = NormalizeOptional(contactPersonPhone),
            Address = address.Trim(),
            City = city.Trim(),
            State = state.Trim(),
            Pin = pin.Trim(),
            IsActive = isActive,
            IsPreferred = isPreferred,
        };
    }

    public void Update(
        string name,
        string? contactPersonName,
        string? contactPersonPhone,
        string address,
        string city,
        string state,
        string pin,
        bool isActive,
        bool isPreferred)
    {
        Name = name.Trim();
        ContactPersonName = NormalizeOptional(contactPersonName);
        ContactPersonPhone = NormalizeOptional(contactPersonPhone);
        Address = address.Trim();
        City = city.Trim();
        State = state.Trim();
        Pin = pin.Trim();
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