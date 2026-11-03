namespace Intelibill.Domain.Enums;

/// <summary>How the approved refund of a sale return was settled with the customer.</summary>
public enum ReturnSettlementKind
{
    /// <summary>Reduced what the customer still owed on credit.</summary>
    ReceivableReduction = 1,

    /// <summary>Paid back outside the shop's books (tender not recorded).</summary>
    ExternalRefund = 2,

    /// <summary>Issued as store credit (a credit note the customer can redeem).</summary>
    StoreCreditIssued = 3,
}
