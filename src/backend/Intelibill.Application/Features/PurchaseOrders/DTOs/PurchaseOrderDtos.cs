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
    int LineCount,
    decimal ExpectedTotal,
    DateTimeOffset CreatedAt);

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
