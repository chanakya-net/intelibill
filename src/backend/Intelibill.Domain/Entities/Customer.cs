using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

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

    /// <summary>How new due is judged against <see cref="CreditLimit"/>.</summary>
    public CreditPolicyMode CreditPolicyMode { get; private set; } = CreditPolicyMode.LegacyAdvisory;

    /// <summary>Days after the issue date a due sale falls due; null when no terms were agreed.</summary>
    public int? CreditTermsDays { get; private set; }

    public const int MaxCreditTermsDays = 365;

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

    /// <summary>
    /// Sets the credit policy and terms against the current <see cref="CreditLimit"/>: an enforced
    /// policy needs a positive limit, so a zero limit can never mean "unlimited".
    /// </summary>
    public ErrorOr<Success> SetCreditPolicy(CreditPolicyMode mode, int? creditTermsDays)
    {
        if (!Enum.IsDefined(mode))
            return Errors.CustomerCredit.PolicyModeInvalid;

        if (mode == CreditPolicyMode.Enforced && CreditLimit <= 0m)
            return Errors.CustomerCredit.EnforcedPolicyRequiresLimit;

        if (creditTermsDays is < 0 or > MaxCreditTermsDays)
            return Errors.CustomerCredit.TermsDaysInvalid;

        CreditPolicyMode = mode;
        CreditTermsDays = creditTermsDays;
        return Result.Success;
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
