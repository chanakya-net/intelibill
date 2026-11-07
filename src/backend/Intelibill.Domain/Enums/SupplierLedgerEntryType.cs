namespace Intelibill.Domain.Enums;

public enum SupplierLedgerEntryType
{
    GoodsReceived = 1,
    PaymentMade = 2,
    RecordAdjusted = 3,
    Reversal = 4,

    /// <summary>Signed difference posted when a receipt's total cost is corrected.</summary>
    CostAdjusted = 5,
    SupplierReturnCredit = 6,
}
