using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class ItemRepository(ApplicationDbContext context)
    : RepositoryBase<Item>(context), IItemRepository
{
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
}
