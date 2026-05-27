using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Intelibill.Application.Features.Items.Queries.GetItems;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class ItemRepository(ApplicationDbContext context)
    : RepositoryBase<Item>(context), IItemRepository, IItemCatalogRepository
{
    private readonly ApplicationDbContext _context = context;

    public async Task<Item?> GetByBarcodeAsync(Guid shopId, string barcode, CancellationToken cancellationToken = default) =>
        await DbSet.FirstOrDefaultAsync(i => i.ShopId == shopId && i.Barcode == barcode, cancellationToken);

    public async Task<Item?> GetByNameAsync(Guid shopId, string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = name.Trim();
        return await DbSet.FirstOrDefaultAsync(i => i.ShopId == shopId && i.Name == normalizedName, cancellationToken);
    }

    public async Task<IReadOnlyList<Item>> GetByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(i => i.ShopId == shopId)
            .OrderBy(i => i.Name)
            .ToListAsync(cancellationToken);

    public IAsyncEnumerable<Item> StreamByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        DbSet
            .Where(i => i.ShopId == shopId)
            .OrderBy(i => i.Name)
            .AsAsyncEnumerable();

    public async Task<IReadOnlyList<Item>> GetByBarcodesAsync(Guid shopId, IReadOnlyList<string> barcodes, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(i => i.ShopId == shopId && barcodes.Contains(i.Barcode))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Item>> GetByIdsAsync(Guid shopId, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken = default)
    {
        var items = await DbSet
            .Where(i => i.ShopId == shopId && itemIds.Contains(i.Id))
            .ToListAsync(cancellationToken);

        if (items.Count > 0 || itemIds.Count == 0)
        {
            return items;
        }

        // Fallback for legacy or inconsistent records where shop linkage drifted.
        return await DbSet
            .Where(i => itemIds.Contains(i.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<ItemCatalogResultReadModel> GetCatalogAsync(
        ItemCatalogFilter filter,
        CancellationToken cancellationToken = default)
    {
        var normalizedSearch = filter.Search?.Trim();
        var normalizedStatus = filter.Status?.Trim().ToLowerInvariant();

        var catalogQuery = ApplyStatusFilter(
            ApplySearchFilter(_context.Items.AsNoTracking().Where(i => i.ShopId == filter.ShopId), normalizedSearch),
            normalizedStatus);

        var summaryQuery = catalogQuery
            .Select(i => new
            {
                i.Id,
                i.IsActive,
                Quantity = _context.Inventory
                    .Where(inv => inv.ItemId == i.Id)
                    .Select(inv => (decimal?)inv.Quantity)
                    .FirstOrDefault(),
                ReorderLevel = _context.Inventory
                    .Where(inv => inv.ItemId == i.Id)
                    .Select(inv => (decimal?)inv.ReorderLevel)
                    .FirstOrDefault(),
                SalesPrice = _context.InventoryBatches
                    .Where(b => b.ItemId == i.Id && !b.IsVoided)
                    .OrderByDescending(b => b.CreatedAt)
                    .ThenByDescending(b => b.Id)
                    .Select(b => (decimal?)b.SalesPrice)
                    .FirstOrDefault()
            });

        var summaryItems = await summaryQuery.ToListAsync(cancellationToken);

        var totalCount = summaryItems.Count;
        var activeCount = 0;
        var inactiveCount = 0;
        var runningLowStockCount = 0;
        var criticalStockCount = 0;
        var totalStockValue = 0m;

        foreach (var item in summaryItems)
        {
            if (item.IsActive)
                activeCount++;
            else
                inactiveCount++;

            var currentStock = item.Quantity ?? 0m;
            var reorderLevel = item.ReorderLevel ?? 0m;
            var unitPrice = item.SalesPrice ?? 0m;

            totalStockValue += currentStock * unitPrice;

            var stockStatus = DeriveStockStatus(currentStock, reorderLevel);
            if (stockStatus == StockStatusRunningLow)
                runningLowStockCount++;
            else if (stockStatus == StockStatusCritical)
                criticalStockCount++;
        }

        var summary = new ItemCatalogSummaryReadModel(
            TotalItems: totalCount,
            ActiveItems: activeCount,
            InactiveItems: inactiveCount,
            RunningLowStockCount: runningLowStockCount,
            CriticalStockCount: criticalStockCount,
            TotalStockValue: totalStockValue);

        var pagedItems = await catalogQuery
            .OrderBy(i => i.Name)
            .ThenBy(i => i.Barcode)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var itemIds = pagedItems.Select(item => item.Id).ToList();
        if (itemIds.Count == 0)
        {
            return new ItemCatalogResultReadModel([], totalCount, summary);
        }

        var inventoryLookup = await _context.Inventory
            .AsNoTracking()
            .Where(i => itemIds.Contains(i.ItemId))
            .ToDictionaryAsync(i => i.ItemId, i => i, cancellationToken);

        var latestBatchPrices = await _context.InventoryBatches
            .AsNoTracking()
            .Where(b => !b.IsVoided && itemIds.Contains(b.ItemId))
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Id)
            .Select(b => new
            {
                b.ItemId,
                b.SalesPrice,
            })
            .ToListAsync(cancellationToken);

        var unitPriceLookup = latestBatchPrices
            .GroupBy(b => b.ItemId)
            .ToDictionary(g => g.Key, g => g.First().SalesPrice);

        var catalog = pagedItems
            .Select(item =>
            {
                var currentStock = inventoryLookup.TryGetValue(item.Id, out var inventory)
                    ? inventory.Quantity
                    : 0m;
                var reorderLevel = inventory?.ReorderLevel ?? 0m;
                var unitPrice = unitPriceLookup.GetValueOrDefault(item.Id, 0m);
                var stockStatus = DeriveStockStatus(currentStock, reorderLevel);

                return new ItemCatalogReadModel(
                    item.Id,
                    item.Name,
                    item.Barcode,
                    item.Description,
                    item.Uom,
                    item.IsActive,
                    currentStock,
                    unitPrice,
                    currentStock * unitPrice,
                    reorderLevel,
                    stockStatus,
                    item.HsnCode,
                    item.DefaultTaxRatePercent,
                    item.DefaultTaxIncluded);
            })
            .ToList();

        return new ItemCatalogResultReadModel(catalog, totalCount, summary);
    }

    private static IQueryable<Item> ApplySearchFilter(IQueryable<Item> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return query;

        var pattern = $"%{search.ToLowerInvariant()}%";

        return query.Where(i =>
            EF.Functions.ILike(i.Name, pattern)
            || EF.Functions.ILike(i.Barcode, pattern)
            || (i.Description != null && EF.Functions.ILike(i.Description, pattern))
            || (i.Uom != null && EF.Functions.ILike(i.Uom, pattern))
            || (i.HsnCode != null && EF.Functions.ILike(i.HsnCode, pattern)));
    }

    private static IQueryable<Item> ApplyStatusFilter(IQueryable<Item> query, string? status)
    {
        if (string.IsNullOrWhiteSpace(status) || status == "all")
            return query;

        return status switch
        {
            "active" => query.Where(i => i.IsActive),
            "inactive" => query.Where(i => !i.IsActive),
            _ => query.Where(_ => false),
        };
    }

    private const string StockStatusRunningLow = "runningLow";
    private const string StockStatusCritical = "critical";

    private static string DeriveStockStatus(decimal currentStock, decimal reorderLevel)
    {
        if (currentStock <= 0m)
            return StockStatusCritical;

        if (currentStock <= reorderLevel)
            return StockStatusRunningLow;

        return "inStock";
    }
}
