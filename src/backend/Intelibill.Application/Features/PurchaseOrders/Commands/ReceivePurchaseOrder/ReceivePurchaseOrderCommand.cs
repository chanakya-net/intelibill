namespace Intelibill.Application.Features.PurchaseOrders.Commands.ReceivePurchaseOrder;

public sealed record ReceivePurchaseOrderLineInput(
    Guid PurchaseOrderLineId,
    string BatchNumber,
    int Quantity,
    decimal TotalPurchaseCost,
    decimal Mrp,
    decimal SalesPrice,
    decimal TaxRatePercent,
    bool TaxIncluded,
    bool PurchaseTaxIncluded,
    DateOnly? ExpiryDate,
    DateOnly? ManufacturingDate);

public sealed record ReceivePurchaseOrderCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid PurchaseOrderId,
    string? ReferenceNumber,
    string? Notes,
    DateTimeOffset? ReceivedAt,
    IReadOnlyList<ReceivePurchaseOrderLineInput> Lines);
