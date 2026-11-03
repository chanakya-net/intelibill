namespace Intelibill.Domain.Enums;

/// <summary>Whether a sale return's financial adjustment can be exported as a voucher.</summary>
public enum ReturnAccountingStatus
{
    /// <summary>The posted snapshot balances: line adjustments and settlement legs both equal the refund.</summary>
    Balanced = 1,

    /// <summary>No trustworthy snapshot (legacy evidence is missing); exports name it instead of posting it.</summary>
    NeedsReview = 2,
}
