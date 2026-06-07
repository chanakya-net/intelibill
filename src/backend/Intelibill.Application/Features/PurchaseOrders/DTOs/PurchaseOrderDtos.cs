using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.PurchaseOrders.DTOs;

public sealed record PurchaseOrderLineDto(
    Guid LineId,
    Guid ItemId,
    string Description,
    int ExpectedQuantity,
    int ReceivedQuantity,
    int RemainingQuantity,
    decimal UnitCost,
    decimal LineTotal);

public sealed record PurchaseOrderReceiptLineDto(
    Guid ReceiptLineId,
    Guid PurchaseOrderLineId,
    Guid ItemId,
    Guid InventoryBatchId,
    string? BatchNumber,
    bool? BatchVoided,
    Guid StockTransactionId,
    decimal Quantity,
    decimal TotalPurchaseCost,
    decimal UnitCost,
    decimal Mrp,
    decimal SalesPrice,
    decimal TaxRatePercent,
    bool TaxIncluded,
    bool PurchaseTaxIncluded);

public sealed record PurchaseOrderReceiptDto(
    Guid ReceiptId,
    string ReceiptNumber,
    DateTimeOffset ReceivedAt,
    string? ReferenceNumber,
    string? Notes,
    Guid ReceivedByUserId,
    string? ReceivedByDisplayName,
    IReadOnlyList<PurchaseOrderReceiptLineDto> Lines);

public sealed record PurchaseOrderListItemDto(
    Guid PurchaseOrderId,
    string PurchaseOrderNumber,
    PurchaseOrderStatus Status,
    string? SupplierName,
    string? SupplierReference,
    int LineCount,
    int ExpectedQuantity,
    int ReceivedQuantity,
    decimal ExpectedTotal,
    DateTimeOffset CreatedAt);

public sealed record PurchaseOrderPagedResultDto(
    IReadOnlyList<PurchaseOrderListItemDto> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);

public sealed record PurchaseOrderDetailDto(
    Guid PurchaseOrderId,
    string PurchaseOrderNumber,
    PurchaseOrderStatus Status,
    Guid? SupplierId,
    DateOnly? OrderDate,
    DateOnly? ExpectedDeliveryDate,
    string? SupplierReferenceNumber,
    string? Notes,
    IReadOnlyList<PurchaseOrderLineDto> Lines,
    decimal ExpectedTotal,
    DateTimeOffset CreatedAt,
    string? CancellationReason = null,
    string? SupplierName = null,
    string? SupplierReference = null,
    int ReceivedQuantity = 0,
    IReadOnlyList<PurchaseOrderReceiptDto>? Receipts = null,
    DateTimeOffset? ClosedAt = null,
    Guid? ClosedBy = null,
    string? CloseReason = null);
