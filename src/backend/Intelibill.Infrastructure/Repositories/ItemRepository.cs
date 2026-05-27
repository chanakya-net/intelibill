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

        IQueryable<Item> itemQuery = _context.Items
            .AsNoTracking()
            .Where(i => i.ShopId == filter.ShopId);

        itemQuery = ApplySearchFilter(itemQuery, normalizedSearch);

        var filteredItems = ApplyStatusFilter(itemQuery, normalizedStatus);

        var totalCount = await filteredItems.CountAsync(cancellationToken);

        var summaryRows = await filteredItems
            .Select(item => new CatalogRow(
                item.Id,
                item.Name,
                item.Barcode,
                item.Description,
                item.Uom,
                item.IsActive,
                CurrentStock: _context.Inventory
                    .Where(inv => inv.ShopId == item.ShopId && inv.ItemId == item.Id)
                    .Select(inv => (decimal?)inv.Quantity)
                    .FirstOrDefault() ?? 0m,
                ReorderLevel: _context.Inventory
                    .Where(inv => inv.ShopId == item.ShopId && inv.ItemId == item.Id)
                    .Select(inv => (decimal?)inv.ReorderLevel)
                    .FirstOrDefault() ?? 0m,
                UnitPrice: _context.InventoryBatches
                    .Where(batch => batch.ShopId == item.ShopId && batch.ItemId == item.Id && !batch.IsVoided)
                    .OrderByDescending(batch => batch.UpdatedAt ?? batch.CreatedAt)
                    .ThenByDescending(batch => batch.Id)
                    .Select(batch => (decimal?)batch.SalesPrice)
                    .FirstOrDefault(),
                CurrentStockValue: _context.InventoryBatches
                    .Where(batch => batch.ShopId == item.ShopId && batch.ItemId == item.Id && !batch.IsVoided)
                    .Select(batch => (decimal?)(batch.Quantity * batch.SalesPrice))
                    .Sum() ?? 0m,
                item.HsnCode,
                item.DefaultTaxRatePercent,
                item.DefaultTaxIncluded))
            .ToListAsync(cancellationToken);

        var summary = new ItemCatalogSummaryReadModel(
            TotalItems: summaryRows.Count,
            ActiveItems: summaryRows.Count(r => r.IsActive),
            InactiveItems: summaryRows.Count(r => !r.IsActive),
            RunningLowStockCount: summaryRows.Count(r => r.IsActive && r.CurrentStock > 0m && r.CurrentStock <= r.ReorderLevel),
            CriticalStockCount: summaryRows.Count(r => r.IsActive && r.CurrentStock <= 0m),
            TotalStockValue: summaryRows.Sum(r => r.CurrentStockValue));

        var pageRows = await filteredItems
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Barcode)
            .ThenBy(item => item.Id)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(item => new CatalogRow(
                item.Id,
                item.Name,
                item.Barcode,
                item.Description,
                item.Uom,
                item.IsActive,
                CurrentStock: _context.Inventory
                    .Where(inv => inv.ShopId == item.ShopId && inv.ItemId == item.Id)
                    .Select(inv => (decimal?)inv.Quantity)
                    .FirstOrDefault() ?? 0m,
                ReorderLevel: _context.Inventory
                    .Where(inv => inv.ShopId == item.ShopId && inv.ItemId == item.Id)
                    .Select(inv => (decimal?)inv.ReorderLevel)
                    .FirstOrDefault() ?? 0m,
                UnitPrice: _context.InventoryBatches
                    .Where(batch => batch.ShopId == item.ShopId && batch.ItemId == item.Id && !batch.IsVoided)
                    .OrderByDescending(batch => batch.UpdatedAt ?? batch.CreatedAt)
                    .ThenByDescending(batch => batch.Id)
                    .Select(batch => (decimal?)batch.SalesPrice)
                    .FirstOrDefault(),
                CurrentStockValue: _context.InventoryBatches
                    .Where(batch => batch.ShopId == item.ShopId && batch.ItemId == item.Id && !batch.IsVoided)
                    .Select(batch => (decimal?)(batch.Quantity * batch.SalesPrice))
                    .Sum() ?? 0m,
                item.HsnCode,
                item.DefaultTaxRatePercent,
                item.DefaultTaxIncluded))
            .ToListAsync(cancellationToken);

        var catalog = pageRows
            .Select(item => new ItemCatalogReadModel(
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
                DeriveStockStatus(item.IsActive, item.CurrentStock, item.ReorderLevel),
                item.HsnCode,
                item.DefaultTaxRatePercent,
                item.DefaultTaxIncluded))
            .ToList();

        return new ItemCatalogResultReadModel(catalog, totalCount, summary);
    }

    private static IQueryable<Item> ApplySearchFilter(IQueryable<Item> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return query;

        var pattern = $"%{search.Trim()}%";

        return query.Where(i =>
            EF.Functions.ILike(i.Name, pattern)
            || EF.Functions.ILike(i.Barcode, pattern));
    }

    private IQueryable<Item> ApplyStatusFilter(IQueryable<Item> query, string? status)
    {
        if (string.IsNullOrWhiteSpace(status) || status == "all")
            return query;

        return status switch
        {
            "active" => query.Where(i => i.IsActive),
            "inactive" => query.Where(i => !i.IsActive),
            "instock" => query.Where(i =>
                i.IsActive
                && (
                    (_context.Inventory
                        .Where(inv => inv.ShopId == i.ShopId && inv.ItemId == i.Id)
                        .Select(inv => (decimal?)inv.Quantity)
                        .FirstOrDefault() ?? 0m)
                    >
                    (_context.Inventory
                        .Where(inv => inv.ShopId == i.ShopId && inv.ItemId == i.Id)
                        .Select(inv => (decimal?)inv.ReorderLevel)
                        .FirstOrDefault() ?? 0m)
                )),
            "reorder" => query.Where(i =>
                i.IsActive
                && (
                    (_context.Inventory
                        .Where(inv => inv.ShopId == i.ShopId && inv.ItemId == i.Id)
                        .Select(inv => (decimal?)inv.Quantity)
                        .FirstOrDefault() ?? 0m) > 0m
                )
                && (
                    (_context.Inventory
                        .Where(inv => inv.ShopId == i.ShopId && inv.ItemId == i.Id)
                        .Select(inv => (decimal?)inv.Quantity)
                        .FirstOrDefault() ?? 0m)
                    <=
                    (_context.Inventory
                        .Where(inv => inv.ShopId == i.ShopId && inv.ItemId == i.Id)
                        .Select(inv => (decimal?)inv.ReorderLevel)
                        .FirstOrDefault() ?? 0m)
                )),
            "outofstock" => query.Where(i =>
                i.IsActive
                && (
                    (_context.Inventory
                        .Where(inv => inv.ShopId == i.ShopId && inv.ItemId == i.Id)
                        .Select(inv => (decimal?)inv.Quantity)
                        .FirstOrDefault() ?? 0m) <= 0m
                )),
            _ => query.Where(_ => false),
        };
    }

    private static string DeriveStockStatus(bool isActive, decimal currentStock, decimal reorderLevel)
    {
        if (!isActive)
            return "inactive";

        if (currentStock <= 0m)
            return "outOfStock";

        if (currentStock <= reorderLevel)
            return "reorder";

        return "inStock";
    }

    private sealed record CatalogRow(
        Guid Id,
        string Name,
        string Barcode,
        string? Description,
        string Uom,
        bool IsActive,
        decimal CurrentStock,
        decimal ReorderLevel,
        decimal? UnitPrice,
        decimal CurrentStockValue,
        string? HsnCode,
        decimal DefaultTaxRatePercent,
        bool DefaultTaxIncluded);
}
