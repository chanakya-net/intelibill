using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Expenses.DTOs;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Queries.GetAdjustmentHistory;

public sealed class GetAdjustmentHistoryQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IInventoryAdjustmentRepository adjustmentRepository)
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public async Task<ErrorOr<PaginatedList<InventoryAdjustmentHistoryDto>>> Handle(
        GetAdjustmentHistoryQuery query,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(query.UserId, cancellationToken);
        if (user is null)
            return Error.NotFound("User.NotFound", "User not found.");

        var shop = await shopRepository.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(query.UserId, query.ShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var pageNumber = Math.Max(query.PageNumber, 1);
        var pageSize = NormalizePageSize(query.PageSize);

        var filter = new InventoryAdjustmentHistoryFilter(
            query.ShopId,
            pageNumber,
            pageSize,
            query.ItemId,
            query.BatchId,
            query.Direction,
            query.Reason,
            query.From,
            query.To,
            query.IncludeVoided);

        var (items, totalCount) = await adjustmentRepository.GetHistoryAsync(filter, cancellationToken);
        var dtos = items.Select(MapToDto).ToList();

        return new PaginatedList<InventoryAdjustmentHistoryDto>(dtos, totalCount, pageNumber, pageSize);
    }

    private static int NormalizePageSize(int pageSize)
    {
        if (pageSize <= 0)
            return DefaultPageSize;

        return Math.Min(pageSize, MaxPageSize);
    }

    private static InventoryAdjustmentHistoryDto MapToDto(InventoryAdjustmentHistoryReadModel item) =>
        new(
            item.AdjustmentId,
            item.AdjustmentNumber,
            item.ItemId,
            item.ItemName,
            item.Barcode,
            item.BatchId,
            item.BatchNumber,
            item.Direction.ToString(),
            item.Reason.ToString(),
            item.Quantity,
            item.UnitCost,
            item.CostImpact,
            item.Notes,
            item.PerformedAt,
            item.CreatedAt,
            item.PerformedByUserId,
            item.PerformedByDisplayName,
            item.IsVoided,
            item.VoidedAt,
            item.VoidedByUserId,
            item.VoidedByDisplayName,
            item.VoidReason,
            item.ReversalStockTransactionId);
}
