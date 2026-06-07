using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.DTOs;
using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.PurchaseOrders.Queries.GetPurchaseOrders;

public sealed record GetPurchaseOrdersQuery(
    Guid ActorUserId,
    Guid ActiveShopId,
    string? Search = null,
    PurchaseOrderStatus? Status = null,
    DateOnly? OrderDateFrom = null,
    DateOnly? OrderDateTo = null,
    int Page = 1,
    int PageSize = 20);

public sealed class GetPurchaseOrdersQueryHandler(
    IUserRepository userRepository,
    IPurchaseOrderRepository purchaseOrderRepository)
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public async Task<ErrorOr<PurchaseOrderPagedResultDto>> HandleAsync(
        GetPurchaseOrdersQuery query,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(query.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var membership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == query.ActiveShopId);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var pageNumber = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? DefaultPageSize : Math.Min(query.PageSize, MaxPageSize);
        var filter = new PurchaseOrderListFilter(
            query.ActiveShopId,
            string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            query.Status,
            query.OrderDateFrom,
            query.OrderDateTo,
            pageNumber,
            pageSize);
        var result = await purchaseOrderRepository.GetByShopAsync(filter, cancellationToken);

        return new PurchaseOrderPagedResultDto(
            result.Items.Select(PurchaseOrderDtoMapper.ToListItem).ToList(),
            result.TotalCount,
            pageNumber,
            pageSize);
    }
}
