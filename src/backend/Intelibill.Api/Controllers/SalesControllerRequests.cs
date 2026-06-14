using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Api.Controllers;

public sealed record RecordSaleRequest(
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    string IdempotencyKey,
    PaymentMethod PaymentMethod,
    decimal PaidAmount,
    decimal DueAmount,
    IReadOnlyList<RecordSaleItemRequest> Items,
    InstantDiscountRequest? SaleDiscount = null,
    decimal CreditNoteAppliedAmount = 0m,
    IReadOnlyList<CreditNoteRedemptionRequest>? CreditNoteRedemptions = null);

public sealed record ReserveInvoiceLeaseRequest(
    string DeviceId,
    int? BlockSize = null);

public sealed record OfflineSalesSyncRequest(
    string DeviceId,
    IReadOnlyList<OfflineSaleSyncRequest> Sales);

public sealed record OfflineSaleSyncRequest(
    string ClientSaleId,
    string InvoiceNumber,
    DateTimeOffset SoldAt,
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    PaymentMethod PaymentMethod,
    decimal PaidAmount,
    decimal DueAmount,
    decimal SubtotalBeforeDiscount,
    decimal TotalBeforeDiscount,
    decimal TotalDiscountAmount,
    decimal TotalTaxAmount,
    decimal TotalAmount,
    InstantDiscountType SaleDiscountOverrideType,
    decimal SaleDiscountOverrideValue,
    Guid? ConfiguredSaleRuleId,
    DiscountRuleType? ConfiguredSaleRuleType,
    decimal? ConfiguredSaleRulePercentage,
    decimal? ConfiguredSaleRuleThresholdAmount,
    IReadOnlyList<OfflineSaleSyncLineRequest> Items);

public sealed record OfflineSaleSyncLineRequest(
    string Barcode,
    string BatchNumber,
    string ItemName,
    decimal Quantity,
    decimal CostPrice,
    decimal SalesPrice,
    decimal Mrp,
    decimal TaxRatePercent,
    bool IsPriceIncludingTax,
    Guid InventoryBatchId,
    decimal PreTaxAmountBeforeDiscount,
    decimal ItemDiscountAmount,
    decimal SaleDiscountAmount,
    decimal TaxableAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    Guid? ConfiguredBatchRuleId,
    decimal? ConfiguredBatchRulePercentage,
    InstantDiscountType ItemDiscountOverrideType,
    decimal ItemDiscountOverrideValue,
    string? HsnCode);

public sealed record RecordSaleItemRequest(
    string Barcode,
    string BatchNumber,
    string ItemName,
    decimal Quantity,
    decimal CostPrice,
    decimal SalesPrice,
    decimal Mrp,
    decimal TaxRatePercent,
    bool IsPriceIncludingTax,
    Guid InventoryBatchId,
    InstantDiscountRequest? ItemDiscount = null,
    string? ClientLineKey = null,
    string? HsnCode = null,
    SaleLineType LineType = SaleLineType.Goods,
    Guid? ServiceId = null);

public sealed record PreviewSaleRequest(
    InstantDiscountRequest SaleDiscount,
    IReadOnlyList<PreviewSaleItemRequest> Items);

public sealed record PreviewSaleItemRequest(
    Guid InventoryBatchId,
    string Barcode,
    string BatchNumber,
    string ItemName,
    decimal Quantity,
    decimal CostPrice,
    decimal SalesPrice,
    decimal Mrp,
    decimal TaxRatePercent,
    bool IsPriceIncludingTax,
    InstantDiscountRequest ItemDiscount,
    string? ClientLineKey = null,
    string? HsnCode = null,
    SaleLineType LineType = SaleLineType.Goods,
    Guid? ServiceId = null);

public sealed record InstantDiscountRequest(
    InstantDiscountType Type,
    decimal Value);

public sealed record CreditNoteRedemptionRequest(
    string Code,
    decimal Amount);

public sealed record PreviewSaleReturnRequest(
    decimal? DueReductionOverrideAmount,
    string? DueOverrideReason,
    IReadOnlyList<PreviewSaleReturnItemRequest> Items);

public sealed record PreviewSaleReturnItemRequest(
    Guid SaleItemId,
    decimal Quantity,
    SaleReturnCondition? Condition,
    decimal? ApprovedRefundAmount,
    string? Notes,
    SaleLineType LineType = SaleLineType.Goods);

public sealed record RecordSaleReturnRequest(
    ReturnPayoutDestination? PayoutDestination,
    decimal? DueReductionOverrideAmount,
    string? DueOverrideReason,
    string? Notes,
    IReadOnlyList<RecordSaleReturnItemRequest> Items,
    PaymentMethod? PayoutMethod = null,
    DateTimeOffset? CreditNoteExpiresAt = null,
    string? CreditNoteReason = null);

public sealed record RecordSaleReturnItemRequest(
    Guid SaleItemId,
    decimal Quantity,
    SaleReturnCondition? Condition,
    decimal? ApprovedRefundAmount,
    string? Notes,
    SaleLineType LineType = SaleLineType.Goods);

public sealed record VoidSaleReturnRequest(string Reason);
