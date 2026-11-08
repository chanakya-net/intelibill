namespace Intelibill.Domain.Enums;

/// <summary>
/// A non-repeatable financial command that carries a durable operation key. Stored as its number;
/// never renumber a member, because committed receipts keep it for the retention period.
/// </summary>
public enum FinancialOperationType
{
    CustomerPayment = 1,
    SupplierPayment = 2,

    /// <summary>The ordered row ids and row hashes of one keyed bulk stock intake.</summary>
    BulkIntakeManifest = 3,

    /// <summary>One committed row of a keyed bulk stock intake.</summary>
    BulkIntakeRow = 4,

    /// <summary>One physical stock count of a batch.</summary>
    StockCount = 5,

    /// <summary>One correction of a purchase receipt's total cost.</summary>
    ReceiptCostCorrection = 6,

    /// <summary>One resolution of a reconciliation case.</summary>
    ReconciliationResolution = 7,

    /// <summary>One support recovery bundle row submitted by an owner or manager.</summary>
    OfflineRecoverySubmission = 8,

    /// <summary>One allocation of a recorded payment receipt to invoices or purchase receipts.</summary>
    PaymentAllocation = 9,

    /// <summary>One reversal of a recorded payment receipt.</summary>
    PaymentReversal = 10,

    /// <summary>One close of a monthly posting period.</summary>
    PostingPeriodClose = 11,

    /// <summary>One reopening of a closed monthly posting period.</summary>
    PostingPeriodReopen = 12,

    /// <summary>One atomic purchase order receipt, including all of its lots.</summary>
    PurchaseOrderReceipt = 13,
    SupplierReturn = 14,
    SupplierReturnCredit = 15,
    PurchaseReceiptReversal = 16,
    SaleCancellation = 17,
    TillCreate = 18,
    TillOpen = 19,
    TillCashMovement = 20,
    TillClose = 21,
    TillCloseReview = 22,
    ExpenseRecord = 23,
    ExpenseCorrection = 24,
    SaleReturn = 25,
    SaleReturnVoid = 26,
    BatchDisposition = 27,
}
