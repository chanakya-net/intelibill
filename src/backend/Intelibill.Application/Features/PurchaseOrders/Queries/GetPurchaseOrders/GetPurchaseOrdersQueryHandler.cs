using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.DTOs;
using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.PurchaseOrders.Queries.GetPurchaseOrders;

public sealed record GetPurchaseOrdersQuery(
    Guid ActorUserId,
    Guid ActiveShopId,
    int Page = 1,
    int PageSize = 20);

public sealed class GetPurchaseOrdersQueryHandler(
    IUserRepository userRepository,
    IPurchaseOrderRepository purchaseOrderRepository)
{
    public async Task<ErrorOr<IReadOnlyList<PurchaseOrderListItemDto>>> HandleAsync(
        GetPurchaseOrdersQuery query,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(query.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var membership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == query.ActiveShopId);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var orders = await purchaseOrderRepository.GetByShopAsync(
            query.ActiveShopId, query.Page, query.PageSize, cancellationToken);

        return orders.Select(PurchaseOrderDtoMapper.ToListItem).ToList();
    }
}
