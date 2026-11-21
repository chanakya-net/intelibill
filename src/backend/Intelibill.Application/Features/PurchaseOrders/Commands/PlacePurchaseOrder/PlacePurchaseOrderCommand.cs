using Intelibill.Application.Common.Security;

namespace Intelibill.Application.Features.PurchaseOrders.Commands.PlacePurchaseOrder;

public sealed record PlacePurchaseOrderCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid PurchaseOrderId,
    SessionClaims? Session = null);
