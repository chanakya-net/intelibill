using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IInventoryAdjustmentRepository : IRepository<InventoryAdjustment>
{
    Task<InventoryAdjustment?> GetByAdjustmentNumberAsync(Guid shopId, string adjustmentNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventoryAdjustment>> GetByBatchAsync(Guid shopId, Guid inventoryBatchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventoryAdjustment>> GetByShopAndDateRangeAsync(Guid shopId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventoryAdjustment>> GetProfitLossAdjustmentsAsync(Guid shopId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventoryAdjustment>> GetDashboardLossesByShopAndDateRangeAsync(Guid shopId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<InventoryAdjustmentHistoryReadModel> Items, int TotalCount)> GetHistoryAsync(
        InventoryAdjustmentHistoryFilter filter,
        CancellationToken cancellationToken = default);
}

public sealed record InventoryAdjustmentHistoryFilter(
    Guid ShopId,
    int PageNumber,
    int PageSize,
    Guid? ItemId,
    Guid? BatchId,
    InventoryAdjustmentDirection? Direction,
    InventoryAdjustmentReason? Reason,
    DateTimeOffset? From,
    DateTimeOffset? To,
    bool IncludeVoided);

public sealed record InventoryAdjustmentHistoryReadModel(
    Guid AdjustmentId,
    string AdjustmentNumber,
    Guid ItemId,
    string ItemName,
    string Barcode,
    Guid BatchId,
    string BatchNumber,
    InventoryAdjustmentDirection Direction,
    InventoryAdjustmentReason Reason,
    decimal Quantity,
    decimal UnitCost,
    decimal CostImpact,
    string? Notes,
    DateTimeOffset PerformedAt,
    DateTimeOffset CreatedAt,
    Guid PerformedByUserId,
    string PerformedByDisplayName,
    bool IsVoided,
    DateTimeOffset? VoidedAt,
    Guid? VoidedByUserId,
    string? VoidedByDisplayName,
    string? VoidReason,
    Guid? ReversalStockTransactionId);
