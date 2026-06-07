using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.DTOs;
using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.PurchaseOrders.Queries.GetPurchaseOrderDetail;

public sealed record GetPurchaseOrderDetailQuery(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid PurchaseOrderId);

public sealed class GetPurchaseOrderDetailQueryHandler(
    IUserRepository userRepository,
    IPurchaseOrderRepository purchaseOrderRepository)
{
    public async Task<ErrorOr<PurchaseOrderDetailDto>> HandleAsync(
        GetPurchaseOrderDetailQuery query,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(query.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var membership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == query.ActiveShopId);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var po = await purchaseOrderRepository.GetByShopAndIdAsync(
            query.ActiveShopId,
            query.PurchaseOrderId,
            cancellationToken);
        if (po is null)
            return Errors.PurchaseOrder.NotFound;

        return PurchaseOrderDtoMapper.ToDetail(po);
    }
}
