namespace Intelibill.Application.Features.PurchaseReceipts.DTOs;

/// <param name="Delta">Signed change posted to the supplier payable.</param>
/// <param name="ReceiptVersion">The receipt version after the correction.</param>
public sealed record ReceiptCostCorrectionResultDto(
    Guid AdjustmentId,
    Guid ReceiptValueId,
    Guid BatchId,
    decimal PriorTotalCost,
    decimal RevisedTotalCost,
    decimal Delta,
    Guid SupplierLedgerEntryId,
    uint ReceiptVersion,
    DateTimeOffset PerformedAt);
