using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class Customer : BaseEntity
{
    public Guid OwnerUserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string PhoneNumber { get; private set; } = string.Empty;
    public string? Address { get; private set; }
    public bool IsActive { get; private set; }

    private Customer() { }

    public static Customer Create(
        Guid ownerUserId,
        string name,
        string phoneNumber,
        string? address,
        bool isActive = true)
    {
        return new Customer
        {
            OwnerUserId = ownerUserId,
            Name = name.Trim(),
            PhoneNumber = phoneNumber.Trim(),
            Address = NormalizeOptional(address),
            IsActive = isActive,
        };
    }

    public void Update(
        string name,
        string phoneNumber,
        string? address,
        bool isActive)
    {
        Name = name.Trim();
        PhoneNumber = phoneNumber.Trim();
        Address = NormalizeOptional(address);
        IsActive = isActive;
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }
}
