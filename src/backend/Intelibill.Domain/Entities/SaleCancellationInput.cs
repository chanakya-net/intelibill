using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Entities;

/// <summary>Trusted posting facts assembled by the application after authorization and locking.</summary>
public sealed record SaleCancellationInput(
    Guid ShopId,
    Guid SaleId,
    Guid ActorUserId,
    string OperationKey,
    string Reason,
    DateTimeOffset CancelledAt,
    DateOnly BusinessDate,
    string BusinessTimeZoneId,
    string OriginalInvoiceNumber,
    DateOnly OriginalBusinessDate,
    decimal TotalAmount,
    decimal RevenueAmount,
    decimal TaxAmount,
    decimal DueReductionAmount,
    decimal CashRefundAmount,
    Guid? CustomerLedgerEntryId,
    InvoiceSeller Seller,
    InvoiceBuyer Buyer,
    bool GoodsRecoveredConfirmed,
    bool CashRefundConfirmed,
    IReadOnlyList<SaleCancellationLineInput> Lines);

public sealed record SaleCancellationLineInput(
    Guid ShopId,
    Guid SaleId,
    Guid SaleItemId,
    Guid ItemId,
    Guid InventoryBatchId,
    Guid StockTransactionId,
    decimal Quantity,
    string Description,
    string Uqc,
    decimal OriginalCostPrice,
    decimal RevenueAmount,
    decimal RecordedCostAmount);
