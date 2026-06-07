namespace Intelibill.Application.Features.PurchaseOrders.Commands.ClosePurchaseOrder;

public sealed record ClosePurchaseOrderCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid PurchaseOrderId,
    string? Reason);
