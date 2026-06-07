namespace Intelibill.Application.Features.PurchaseOrders.Commands.DeletePurchaseOrderDraft;

public sealed record DeletePurchaseOrderDraftCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid PurchaseOrderId);
