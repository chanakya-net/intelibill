using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class PurchaseOrderSequenceRepository(ApplicationDbContext context)
    : RepositoryBase<PurchaseOrderSequence>(context), IPurchaseOrderSequenceRepository
{
    public async Task<PurchaseOrderSequence?> GetByShopAndYearAsync(
        Guid shopId,
        int year,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .FirstOrDefaultAsync(s => s.ShopId == shopId && s.Year == year, cancellationToken);
}
