using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class ItemRepository(ApplicationDbContext context)
    : RepositoryBase<Item>(context), IItemRepository
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
        var items = await _context.Items
            .AsNoTracking()
            .Where(i => i.ShopId == filter.ShopId)
            .OrderBy(i => i.Name)
            .ThenBy(i => i.Barcode)
            .ToListAsync(cancellationToken);

        var inventories = await _context.Inventory
            .AsNoTracking()
            .Where(i => i.ShopId == filter.ShopId)
            .ToListAsync(cancellationToken);

        var latestBatchPrices = await _context.InventoryBatches
            .AsNoTracking()
            .Where(b => b.ShopId == filter.ShopId && !b.IsVoided)
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Id)
            .Select(b => new
            {
                b.ItemId,
                b.SalesPrice,
            })
            .ToListAsync(cancellationToken);

        var inventoryLookup = inventories.ToDictionary(i => i.ItemId);
        var unitPriceLookup = latestBatchPrices
            .GroupBy(b => b.ItemId)
            .ToDictionary(g => g.Key, g => g.First().SalesPrice);

        var catalog = items
            .Select(item =>
            {
                var currentStock = inventoryLookup.TryGetValue(item.Id, out var inventory)
                    ? inventory.Quantity
                    : 0m;
                var reorderLevel = inventoryLookup.TryGetValue(item.Id, out var inventoryDetails)
                    ? inventoryDetails.ReorderLevel
                    : 0m;
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
            .Where(item => MatchesSearch(item, filter.Search))
            .Where(item => MatchesStatus(item, filter.Status))
            .ToList();

        var totalCount = catalog.Count;
        var pagedItems = catalog
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToList();

        var summary = new ItemCatalogSummaryReadModel(
            TotalItems: items.Count,
            ActiveItems: items.Count(item => item.IsActive),
            InactiveItems: items.Count(item => !item.IsActive),
            RunningLowStockCount: items.Count(item => DeriveStockStatus(
                inventoryLookup.TryGetValue(item.Id, out var inventory) ? inventory.Quantity : 0m,
                inventoryLookup.TryGetValue(item.Id, out var inventoryDetails) ? inventoryDetails.ReorderLevel : 0m) == StockStatusRunningLow),
            CriticalStockCount: items.Count(item => DeriveStockStatus(
                inventoryLookup.TryGetValue(item.Id, out var inventory) ? inventory.Quantity : 0m,
                inventoryLookup.TryGetValue(item.Id, out var inventoryDetails) ? inventoryDetails.ReorderLevel : 0m) == StockStatusCritical),
            TotalStockValue: items.Sum(item =>
            {
                var currentStock = inventoryLookup.TryGetValue(item.Id, out var inventory) ? inventory.Quantity : 0m;
                var unitPrice = unitPriceLookup.GetValueOrDefault(item.Id, 0m);
                return currentStock * unitPrice;
            }));

        return new ItemCatalogResultReadModel(pagedItems, totalCount, summary);
    }

    private static bool MatchesSearch(ItemCatalogReadModel item, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        var term = search.Trim();
        return Contains(item.Name, term)
            || Contains(item.Barcode, term)
            || Contains(item.Description, term)
            || Contains(item.Uom, term)
            || Contains(item.HsnCode, term);
    }

    private static bool MatchesStatus(ItemCatalogReadModel item, string? status)
    {
        if (string.IsNullOrWhiteSpace(status) || status.Equals("all", StringComparison.OrdinalIgnoreCase))
            return true;

        return status.Trim().ToLowerInvariant() switch
        {
            "active" => item.IsActive,
            "inactive" => !item.IsActive,
            _ => true,
        };
    }

    private static bool Contains(string? value, string term) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);

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
