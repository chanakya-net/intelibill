using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class PurchaseOrderRepository(ApplicationDbContext context)
    : RepositoryBase<PurchaseOrder>(context), IPurchaseOrderRepository
{
    public async Task<PurchaseOrder?> GetByShopAndIdAsync(
        Guid shopId,
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(po => po.Lines)
            .FirstOrDefaultAsync(po => po.ShopId == shopId && po.Id == purchaseOrderId, cancellationToken);

    public async Task<PurchaseOrder?> GetDetailAsync(Guid purchaseOrderId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(po => po.Lines)
            .FirstOrDefaultAsync(po => po.Id == purchaseOrderId, cancellationToken);

    public async Task<IReadOnlyList<PurchaseOrder>> GetByShopAsync(
        Guid shopId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(po => po.Lines)
            .Where(po => po.ShopId == shopId)
            .OrderByDescending(po => po.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

    public async Task<int> CountByShopAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        await DbSet.CountAsync(po => po.ShopId == shopId, cancellationToken);
}
