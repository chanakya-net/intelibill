using Intelibill.Application.Common.Security;
using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.PurchaseOrders.DTOs;
using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.PurchaseOrders.Queries.GetPurchaseOrders;

public sealed record GetPurchaseOrdersQuery(
    Guid ActorUserId,
    Guid ActiveShopId,
    string? Search = null,
    PurchaseOrderStatus? Status = null,
    DateOnly? OrderDateFrom = null,
    DateOnly? OrderDateTo = null,
    int Page = 1,
    int PageSize = 20,
    SessionClaims? Session = null);

public sealed class GetPurchaseOrdersQueryHandler(
    PurchaseOrderHistory history,
    IPurchaseOrderRepository purchaseOrderRepository,
    IShopRepository shopRepository,
    IBusinessClock businessClock)
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    [LicenceAction(LicenceAction.History)]
    public Task<ErrorOr<PurchaseOrderPagedResultDto>> HandleAsync(GetPurchaseOrdersQuery query, CancellationToken cancellationToken) =>
        history.ReadAsync(query.ActorUserId, query.ActiveShopId, query.Session, ct => ReadAsync(query, ct), cancellationToken);

    private async Task<ErrorOr<PurchaseOrderPagedResultDto>> ReadAsync(GetPurchaseOrdersQuery query, CancellationToken cancellationToken)
    {
        var window = query.OrderDateFrom is null && query.OrderDateTo is null
            ? (UtcDateWindow?)null
            : await ResolveWindowAsync(query, cancellationToken);
        var pageNumber = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? DefaultPageSize : Math.Min(query.PageSize, MaxPageSize);
        var filter = new PurchaseOrderListFilter(
            query.ActiveShopId,
            string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            query.Status,
            window,
            pageNumber,
            pageSize);
        var result = await purchaseOrderRepository.GetByShopAsync(filter, cancellationToken);

        return new PurchaseOrderPagedResultDto(
            result.Items.Select(PurchaseOrderDtoMapper.ToListItem).ToList(),
            result.TotalCount,
            pageNumber,
            pageSize);
    }

    /// <summary>The order dates are the shop's calendar days; an open end stays open.</summary>
    private async Task<UtcDateWindow?> ResolveWindowAsync(GetPurchaseOrdersQuery query, CancellationToken cancellationToken)
    {
        var shop = await shopRepository.GetByIdAsync(query.ActiveShopId, cancellationToken);
        if (shop is null)
            return null;

        var start = query.OrderDateFrom is { } from
            ? businessClock.GetUtcWindow(from, from, shop.BusinessTimeZoneId).StartInclusive
            : DateTimeOffset.MinValue;
        var end = query.OrderDateTo is { } to
            ? businessClock.GetUtcWindow(to, to, shop.BusinessTimeZoneId).EndExclusive
            : DateTimeOffset.MaxValue;
        return new UtcDateWindow(start, end);
    }
}
