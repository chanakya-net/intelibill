using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.PurchaseOrders.DTOs;

public sealed record PurchaseOrderLineDto(
    Guid LineId,
    Guid ItemId,
    string Description,
    int ExpectedQuantity,
    decimal UnitCost,
    decimal LineTotal);

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
    DateTimeOffset CreatedAt);
