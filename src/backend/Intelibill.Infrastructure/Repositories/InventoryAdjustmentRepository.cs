using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class InventoryAdjustmentRepository : RepositoryBase<InventoryAdjustment>, IInventoryAdjustmentRepository
{
    private readonly ApplicationDbContext _context;

    public InventoryAdjustmentRepository(ApplicationDbContext context)
        : base(context)
    {
        _context = context;
    }

    public async Task<InventoryAdjustment?> GetByAdjustmentNumberAsync(
        Guid shopId,
        string adjustmentNumber,
        CancellationToken cancellationToken = default)
    {
        var normalizedAdjustmentNumber = adjustmentNumber.Trim();
        return await DbSet
            .Include(a => a.Item)
            .Include(a => a.InventoryBatch)
            .FirstOrDefaultAsync(a => a.ShopId == shopId && a.AdjustmentNumber == normalizedAdjustmentNumber, cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryAdjustment>> GetByBatchAsync(
        Guid shopId,
        Guid inventoryBatchId,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(a => a.Item)
            .Where(a => a.ShopId == shopId && a.InventoryBatchId == inventoryBatchId)
            .OrderByDescending(a => a.PerformedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<InventoryAdjustment>> GetByShopAndDateRangeAsync(
        Guid shopId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var start = ToUtcStart(startDate);
        var exclusiveEnd = ToUtcStart(endDate.AddDays(1));

        return await DbSet
            .Include(a => a.Item)
            .Include(a => a.InventoryBatch)
            .Where(a => a.ShopId == shopId
                && a.PerformedAt >= start
                && a.PerformedAt < exclusiveEnd)
            .OrderByDescending(a => a.PerformedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryAdjustment>> GetProfitLossAdjustmentsAsync(
        Guid shopId,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .AsNoTracking()
            .Where(a => a.ShopId == shopId
                && a.Direction == InventoryAdjustmentDirection.Decrease
                && !a.IsVoided)
            .OrderByDescending(a => a.PerformedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<InventoryAdjustment>> GetDashboardLossesByShopAndDateRangeAsync(
        Guid shopId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var start = ToUtcStart(startDate);
        var exclusiveEnd = ToUtcStart(endDate.AddDays(1));

        return await DbSet
            .AsNoTracking()
            .Where(a => a.ShopId == shopId
                && a.Direction == InventoryAdjustmentDirection.Decrease
                && !a.IsVoided
                && a.PerformedAt >= start
                && a.PerformedAt < exclusiveEnd)
            .OrderByDescending(a => a.PerformedAt)
            .ToListAsync(cancellationToken);
    }

    private static DateTime ToUtcStart(DateOnly localDate)
    {
        var localMidnight = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localMidnight, TimeZoneInfo.Local);
    }

    public async Task<(IReadOnlyList<InventoryAdjustmentHistoryReadModel> Items, int TotalCount)> GetHistoryAsync(
        InventoryAdjustmentHistoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = DbSet
            .AsNoTracking()
            .Where(a => a.ShopId == filter.ShopId);

        if (!filter.IncludeVoided)
            query = query.Where(a => !a.IsVoided);

        if (filter.ItemId is { } itemId)
            query = query.Where(a => a.ItemId == itemId);

        if (filter.BatchId is { } batchId)
            query = query.Where(a => a.InventoryBatchId == batchId);

        if (filter.Direction is { } direction)
            query = query.Where(a => a.Direction == direction);

        if (filter.Reason is { } reason)
            query = query.Where(a => a.Reason == reason);

        if (filter.From is { } from)
            query = query.Where(a => a.PerformedAt >= from);

        if (filter.To is { } to)
            query = query.Where(a => a.PerformedAt <= to);

        var totalCount = await query.CountAsync(cancellationToken);

        var pagedRows = await (
                from adjustment in query
                join performer in _context.Users.AsNoTracking()
                    on adjustment.PerformedBy equals performer.Id into performers
                from performer in performers.DefaultIfEmpty()
                join voidedByUser in _context.Users.AsNoTracking()
                    on adjustment.VoidedBy equals voidedByUser.Id into voidedByUsers
                from voidedByUser in voidedByUsers.DefaultIfEmpty()
                orderby adjustment.PerformedAt descending, adjustment.CreatedAt descending
                select new
                {
                    adjustment.Id,
                    adjustment.AdjustmentNumber,
                    adjustment.ItemId,
                    ItemName = adjustment.Item.Name,
                    adjustment.Item.Barcode,
                    BatchId = adjustment.InventoryBatchId,
                    adjustment.InventoryBatch.BatchNumber,
                    adjustment.Direction,
                    adjustment.Reason,
                    adjustment.Quantity,
                    adjustment.UnitCost,
                    adjustment.CostImpact,
                    adjustment.Notes,
                    adjustment.PerformedAt,
                    adjustment.CreatedAt,
                    PerformedByUserId = adjustment.PerformedBy,
                    PerformerFirstName = performer == null ? null : performer.FirstName,
                    PerformerLastName = performer == null ? null : performer.LastName,
                    PerformerEmail = performer == null ? null : performer.Email,
                    PerformerPhoneNumber = performer == null ? null : performer.PhoneNumber,
                    adjustment.IsVoided,
                    adjustment.VoidedAt,
                    VoidedByUserId = adjustment.VoidedBy,
                    VoidedByFirstName = voidedByUser == null ? null : voidedByUser.FirstName,
                    VoidedByLastName = voidedByUser == null ? null : voidedByUser.LastName,
                    VoidedByEmail = voidedByUser == null ? null : voidedByUser.Email,
                    VoidedByPhoneNumber = voidedByUser == null ? null : voidedByUser.PhoneNumber,
                    adjustment.VoidReason,
                    adjustment.ReversalStockTransactionId,
                })
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var items = pagedRows
            .Select(row => new InventoryAdjustmentHistoryReadModel(
                row.Id,
                row.AdjustmentNumber,
                row.ItemId,
                row.ItemName,
                row.Barcode,
                row.BatchId,
                row.BatchNumber,
                row.Direction,
                row.Reason,
                row.Quantity,
                row.UnitCost,
                row.CostImpact,
                row.Notes,
                row.PerformedAt,
                row.CreatedAt,
                row.PerformedByUserId,
                FormatDisplayName(
                    row.PerformerFirstName,
                    row.PerformerLastName,
                    row.PerformerEmail,
                    row.PerformerPhoneNumber,
                    row.PerformedByUserId),
                row.IsVoided,
                row.VoidedAt,
                row.VoidedByUserId,
                row.VoidedByUserId is null
                    ? null
                    : FormatDisplayName(
                        row.VoidedByFirstName,
                        row.VoidedByLastName,
                        row.VoidedByEmail,
                        row.VoidedByPhoneNumber,
                        row.VoidedByUserId.Value),
                row.VoidReason,
                row.ReversalStockTransactionId))
            .ToList();

        return (items, totalCount);
    }

    private static string FormatDisplayName(
        string? firstName,
        string? lastName,
        string? email,
        string? phoneNumber,
        Guid userId)
    {
        var fullName = $"{firstName} {lastName}".Trim();
        if (!string.IsNullOrWhiteSpace(fullName))
            return fullName;

        if (!string.IsNullOrWhiteSpace(email))
            return email;

        return string.IsNullOrWhiteSpace(phoneNumber) ? userId.ToString() : phoneNumber;
    }
}
