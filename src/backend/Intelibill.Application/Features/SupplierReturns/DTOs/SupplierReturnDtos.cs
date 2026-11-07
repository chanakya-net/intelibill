namespace Intelibill.Application.Features.SupplierReturns.DTOs;

public sealed record SupplierReturnLineInput(Guid ReceiptValueId, uint ExpectedReceiptVersion, uint ExpectedBatchVersion, decimal Quantity, decimal ProposedCreditAmount);
public sealed record SupplierReturnCreditLineInput(Guid ReturnLineId, decimal AcceptedAmount);
public sealed record SupplierReturnRequest(string OperationKey, Guid SupplierId, string Reason, bool CreditAccepted, string? SupplierReference, IReadOnlyList<SupplierReturnLineInput> Lines);
public sealed record SupplierReturnCreditRequest(string OperationKey, uint ExpectedVersion, string Reason, string? SupplierReference, IReadOnlyList<SupplierReturnCreditLineInput> Lines);
public sealed record SupplierReturnPreviewLineInput(Guid ReceiptValueId, decimal Quantity);
public sealed record SupplierReturnPreviewRequest(Guid SupplierId, IReadOnlyList<SupplierReturnPreviewLineInput> Lines);
public sealed record ReceiptReversalLineInput(Guid ReceiptValueId, uint ExpectedReceiptVersion, uint ExpectedBatchVersion);
public sealed record ReceiptReversalRequest(string OperationKey, string Reason, uint ExpectedReceiptVersion, uint ExpectedBatchVersion);
public sealed record GroupReceiptReversalRequest(string OperationKey, string Reason, IReadOnlyList<ReceiptReversalLineInput> Lines);
public sealed record SupplierReturnLineDto(Guid ReturnLineId, Guid ReceiptValueId, Guid InventoryBatchId, Guid ItemId, Guid? PurchaseOrderReceiptLineId, decimal Quantity, decimal ProposedCreditAmount, decimal ReceivedQuantity, decimal EffectiveTotalCost, string ItemName, string Uqc, string? SourceReference, Guid StockTransactionId, decimal? AcceptedAmount, Guid? SupplierLedgerEntryId);
public sealed record SupplierReturnCreditDto(Guid CreditId, DateTimeOffset AcceptedAt, DateOnly BusinessDate, Guid AcceptedBy, string Reason, string? SupplierReference);
public sealed record SupplierReturnDetailDto(Guid ReturnId, Guid SupplierId, string ReturnNumber, string Status, uint Version, string Reason, DateTimeOffset ShippedAt, DateOnly BusinessDate, string BusinessTimeZoneId, string ShopName, string SupplierName, IReadOnlyList<SupplierReturnLineDto> Lines, SupplierReturnCreditDto? Credit);
public sealed record PurchaseReceiptDetailDto(Guid ReceiptValueId, Guid InventoryBatchId, Guid ItemId, Guid? SupplierId, Guid? PurchaseOrderReceiptLineId, string BatchNumber, string ItemName, string Uqc, int QuantityScale, uint ReceiptVersion, uint BatchVersion, decimal ReceivedQuantity, decimal AvailableQuantity, decimal ReturnedQuantity, decimal ReversedQuantity, decimal NetRetainedQuantity, decimal ReturnableQuantity, decimal? EffectiveTotalCost, decimal ReturnCreditAmount, decimal AllocatedPaymentAmount, decimal NetPayableAmount, decimal OutstandingAmount, decimal SupplierCreditAmount, decimal SuggestedCreditAmount, bool IsVoided, IReadOnlyList<string> ReversalEligibilityReasons, IReadOnlyList<SupplierReturnDetailDto> Returns, IReadOnlyList<PurchaseReceiptReversalLotDto> Reversals);
public sealed record SupplierReturnPreviewDto(IReadOnlyList<PurchaseReceiptDetailDto> Lines);
public sealed record PurchaseReceiptReversalLotDto(Guid ReversalId, Guid ReceiptValueId, decimal Quantity, decimal EffectiveAmount, Guid StockTransactionId, Guid? SupplierLedgerEntryId, DateTimeOffset ReversedAt, string Reason);
public sealed record PurchaseReceiptReversalResultDto(IReadOnlyList<PurchaseReceiptReversalLotDto> Lots);
