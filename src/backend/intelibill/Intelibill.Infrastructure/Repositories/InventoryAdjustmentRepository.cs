using Intelibill.Domain.ValueObjects;
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

    public async Task<IReadOnlyList<InventoryAdjustment>> GetUnlinkedIncreasesAsync(
        Guid shopId,
        Guid itemId,
        Guid inventoryBatchId,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .AsNoTracking()
            .Where(a => a.ShopId == shopId
                && a.ItemId == itemId
                && a.InventoryBatchId == inventoryBatchId
                && a.Direction == InventoryAdjustmentDirection.Increase
                && (a.Reason == InventoryAdjustmentReason.FoundStock || a.Reason == InventoryAdjustmentReason.OtherGain)
                && !a.IsVoided
                && !_context.ReconciliationIssues.Any(i => i.ShopId == shopId && i.LinkedOperationId == a.Id))
            .OrderByDescending(a => a.PerformedAt)
            .ThenByDescending(a => a.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<InventoryAdjustment>> GetByShopAndDateRangeAsync(
        Guid shopId,
        UtcDateWindow window,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(a => a.Item)
            .Include(a => a.InventoryBatch)
            .Where(a => a.ShopId == shopId
                && a.PerformedAt >= window.StartInclusive
                && a.PerformedAt < window.EndExclusive)
            .OrderByDescending(a => a.PerformedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryAdjustment>> GetPostedInRangeAsync(
        Guid shopId, UtcDateWindow window, CancellationToken cancellationToken = default) =>
        await DbSet.AsNoTracking()
            .Where(a => a.ShopId == shopId
                && ((a.PerformedAt >= window.StartInclusive && a.PerformedAt < window.EndExclusive)
                    || (a.VoidedAt >= window.StartInclusive && a.VoidedAt < window.EndExclusive)))
            .OrderBy(a => a.PerformedAt)
            .ToListAsync(cancellationToken);

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

    public async Task<(IReadOnlyList<InventoryAdjustmentHistoryReadModel> Items, int TotalCount)> GetHistoryAsync(
        InventoryAdjustmentHistoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = BuildHistoryFilterQuery(filter);

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
                select new HistoryRow
                {
                    Id = adjustment.Id,
                    AdjustmentNumber = adjustment.AdjustmentNumber,
                    ItemId = adjustment.ItemId,
                    ItemName = adjustment.Item.Name,
                    Barcode = adjustment.Item.Barcode,
                    BatchId = adjustment.InventoryBatchId,
                    BatchNumber = adjustment.InventoryBatch.BatchNumber,
                    Direction = adjustment.Direction,
                    Reason = adjustment.Reason,
                    Quantity = adjustment.Quantity,
                    UnitCost = adjustment.UnitCost,
                    CostImpact = adjustment.CostImpact,
                    Notes = adjustment.Notes,
                    PerformedAt = adjustment.PerformedAt,
                    CreatedAt = adjustment.CreatedAt,
                    PerformedByUserId = adjustment.PerformedBy,
                    PerformerFirstName = performer == null ? null : performer.FirstName,
                    PerformerLastName = performer == null ? null : performer.LastName,
                    PerformerEmail = performer == null ? null : performer.Email,
                    PerformerPhoneNumber = performer == null ? null : performer.PhoneNumber,
                    IsVoided = adjustment.IsVoided,
                    VoidedAt = adjustment.VoidedAt,
                    VoidedByUserId = adjustment.VoidedBy,
                    VoidedByFirstName = voidedByUser == null ? null : voidedByUser.FirstName,
                    VoidedByLastName = voidedByUser == null ? null : voidedByUser.LastName,
                    VoidedByEmail = voidedByUser == null ? null : voidedByUser.Email,
                    VoidedByPhoneNumber = voidedByUser == null ? null : voidedByUser.PhoneNumber,
                    VoidReason = adjustment.VoidReason,
                    ReversalStockTransactionId = adjustment.ReversalStockTransactionId,
                })
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var items = pagedRows.Select(ToReadModel).ToList();

        return (items, totalCount);
    }

    private IQueryable<InventoryAdjustment> BuildHistoryFilterQuery(InventoryAdjustmentHistoryFilter filter)
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

        return query;
    }

    private static InventoryAdjustmentHistoryReadModel ToReadModel(HistoryRow row)
    {
        return new InventoryAdjustmentHistoryReadModel(
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
            row.ReversalStockTransactionId);
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

    private sealed class HistoryRow
    {
        public Guid Id { get; init; }
        public string AdjustmentNumber { get; init; } = string.Empty;
        public Guid ItemId { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public string Barcode { get; init; } = string.Empty;
        public Guid BatchId { get; init; }
        public string BatchNumber { get; init; } = string.Empty;
        public InventoryAdjustmentDirection Direction { get; init; }
        public InventoryAdjustmentReason Reason { get; init; }
        public decimal Quantity { get; init; }
        public decimal UnitCost { get; init; }
        public decimal CostImpact { get; init; }
        public string? Notes { get; init; }
        public DateTimeOffset PerformedAt { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public Guid PerformedByUserId { get; init; }
        public string? PerformerFirstName { get; init; }
        public string? PerformerLastName { get; init; }
        public string? PerformerEmail { get; init; }
        public string? PerformerPhoneNumber { get; init; }
        public bool IsVoided { get; init; }
        public DateTimeOffset? VoidedAt { get; init; }
        public Guid? VoidedByUserId { get; init; }
        public string? VoidedByFirstName { get; init; }
        public string? VoidedByLastName { get; init; }
        public string? VoidedByEmail { get; init; }
        public string? VoidedByPhoneNumber { get; init; }
        public string? VoidReason { get; init; }
        public Guid? ReversalStockTransactionId { get; init; }
    }
}
