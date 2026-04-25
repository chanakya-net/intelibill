using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class SupplierRepository(ApplicationDbContext context)
    : RepositoryBase<Supplier>(context), ISupplierRepository
{
    public async Task<IReadOnlyList<Supplier>> GetByShopIdAsync(Guid shopId, bool includeSystem, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(s => s.ShopId == shopId && (includeSystem || !s.IsSystem))
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

    public async Task<Supplier?> GetSystemByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(s => s.ShopId == shopId && s.IsSystem)
            .FirstOrDefaultAsync(cancellationToken);
}
