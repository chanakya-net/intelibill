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

    public async Task<IReadOnlyList<Item>> GetByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(i => i.ShopId == shopId)
            .OrderBy(i => i.Name)
            .ToListAsync(cancellationToken);
}