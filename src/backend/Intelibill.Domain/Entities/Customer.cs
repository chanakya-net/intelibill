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

    /// <summary>The buyer's GSTIN when it is a registered business; it makes its invoices B2B.</summary>
    public string? Gstin { get; private set; }

    /// <summary>The buyer's GST state code; defaults to its GSTIN's state.</summary>
    public string? StateCode { get; private set; }

    private Customer() { }

    public static Customer Create(
        Guid shopId,
        string name,
        string phoneNumber,
        string? address,
        bool isActive = true,
        decimal creditLimit = 0m,
        string? gstin = null,
        string? stateCode = null)
    {
        var normalizedGstin = Common.Gstin.Normalize(gstin);
        return new Customer
        {
            ShopId = shopId,
            Name = name.Trim(),
            PhoneNumber = phoneNumber.Trim(),
            Address = NormalizeOptional(address),
            IsActive = isActive,
            CreditLimit = creditLimit,
            Gstin = normalizedGstin,
            StateCode = ResolveStateCode(normalizedGstin, stateCode),
        };
    }

    public void Update(
        string name,
        string phoneNumber,
        string? address,
        bool isActive,
        decimal creditLimit,
        string? gstin = null,
        string? stateCode = null)
    {
        Name = name.Trim();
        PhoneNumber = phoneNumber.Trim();
        Address = NormalizeOptional(address);
        IsActive = isActive;
        CreditLimit = creditLimit;
        Gstin = Common.Gstin.Normalize(gstin);
        StateCode = ResolveStateCode(Gstin, stateCode);
    }

    private static string? ResolveStateCode(string? gstin, string? stateCode) =>
        NormalizeOptional(stateCode) ?? (gstin is { Length: Common.Gstin.Length } ? gstin[..2] : null);

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }
}
