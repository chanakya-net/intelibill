using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

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
    public decimal Amount { get; private set; }
    public SupplierStatus Status { get; private set; }
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
        decimal amount,
        SupplierStatus status,
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
            Amount = amount,
            Status = status,
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
        decimal amount,
        SupplierStatus status,
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
        Amount = amount;
        Status = status;
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
