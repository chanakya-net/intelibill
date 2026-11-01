using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class PurchaseReceiptValueRepository(ApplicationDbContext context)
    : RepositoryBase<PurchaseReceiptValue>(context), IPurchaseReceiptValueRepository
{
    public async Task<PurchaseReceiptValue?> GetByBatchAsync(Guid shopId, Guid inventoryBatchId, CancellationToken cancellationToken = default) =>
        await DbSet.SingleOrDefaultAsync(r => r.ShopId == shopId && r.InventoryBatchId == inventoryBatchId, cancellationToken);

    public async Task<IReadOnlyList<PurchaseReceiptValue>> GetByShopAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        await DbSet.AsNoTracking().Where(r => r.ShopId == shopId).ToListAsync(cancellationToken);
}
