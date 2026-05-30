using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class Customer : BaseEntity
{
    public Guid ShopId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string PhoneNumber { get; private set; } = string.Empty;
    public string? Address { get; private set; }
    public bool IsActive { get; private set; }
    public decimal CreditLimit { get; private set; }

    private Customer() { }

    public static Customer Create(
        Guid shopId,
        string name,
        string phoneNumber,
        string? address,
        bool isActive = true,
        decimal creditLimit = 0m)
    {
        return new Customer
        {
            ShopId = shopId,
            Name = name.Trim(),
            PhoneNumber = phoneNumber.Trim(),
            Address = NormalizeOptional(address),
            IsActive = isActive,
            CreditLimit = creditLimit,
        };
    }

    public void Update(
        string name,
        string phoneNumber,
        string? address,
        bool isActive,
        decimal creditLimit)
    {
        Name = name.Trim();
        PhoneNumber = phoneNumber.Trim();
        Address = NormalizeOptional(address);
        IsActive = isActive;
        CreditLimit = creditLimit;
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }
}
