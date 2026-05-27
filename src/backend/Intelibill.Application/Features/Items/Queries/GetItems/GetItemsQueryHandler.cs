using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Items.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Items.Queries.GetItems;

public sealed class GetItemsQueryHandler(
    IUserRepository userRepository,
    IItemRepository itemRepository)
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public async Task<ErrorOr<ItemCatalogResultDto>> HandleAsync(GetItemsQuery query, CancellationToken cancellationToken)
    {
        var caller = await userRepository.GetByIdWithDetailsAsync(query.UserId, cancellationToken);
        if (caller is null)
            return Errors.Auth.UserNotFound;

        var callerMembership = caller.ShopMemberships.FirstOrDefault(sm => sm.ShopId == query.ActiveShopId);
        if (callerMembership is null)
            return Errors.Shop.MembershipNotFound;

        var pageNumber = query.PageNumber <= 0 ? 1 : query.PageNumber;
        var pageSize = query.PageSize <= 0 ? DefaultPageSize : Math.Min(query.PageSize, MaxPageSize);

        var result = await itemRepository.GetCatalogAsync(
            new ItemCatalogFilter(query.ActiveShopId, query.Search, query.Status, pageNumber, pageSize),
            cancellationToken);

        return new ItemCatalogResultDto(
            Items: result.Items.Select(ToDto).ToList(),
            TotalCount: result.TotalCount,
            PageNumber: pageNumber,
            PageSize: pageSize,
            Summary: new ItemCatalogSummaryDto(
                result.Summary.TotalItems,
                result.Summary.ActiveItems,
                result.Summary.InactiveItems,
                result.Summary.RunningLowStockCount,
                result.Summary.CriticalStockCount,
                result.Summary.TotalStockValue));
    }

    private static ItemDto ToDto(ItemCatalogReadModel item) =>
        new(
            item.Id,
            item.Name,
            item.Barcode,
            item.Description,
            item.Uom,
            item.IsActive,
            item.CurrentStock,
            item.UnitPrice,
            item.CurrentStockValue,
            item.ReorderLevel,
            item.StockStatus,
            item.HsnCode,
            item.DefaultTaxRatePercent,
            item.DefaultTaxIncluded);
}
