namespace Intelibill.Domain.Enums;

/// <summary>The posted state of a sale return's financial adjustment; set once when the return is recorded.</summary>
public enum ReturnAccountingStatus
{
    /// <summary>The posted snapshot balances: line adjustments and settlement legs both equal the refund.</summary>
    Balanced = 1,
}
