namespace Intelibill.Application.Features.PurchaseOrders.Commands.CancelPurchaseOrder;

public sealed record CancelPurchaseOrderCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid PurchaseOrderId,
    string? Reason);
