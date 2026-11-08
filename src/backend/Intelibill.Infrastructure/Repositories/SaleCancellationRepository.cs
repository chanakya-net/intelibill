using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class SaleCancellationRepository(ApplicationDbContext context)
    : RepositoryBase<SaleCancellation>(context), ISaleCancellationRepository
{
    public Task<SaleCancellation?> GetBySaleAsync(Guid shopId, Guid saleId, CancellationToken cancellationToken) =>
        Context.SaleCancellations.AsNoTracking().Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.ShopId == shopId && x.SaleId == saleId, cancellationToken);

    public async Task<IReadOnlyList<SaleCancellation>> GetBySaleIdsAsync(Guid shopId, IReadOnlyCollection<Guid> saleIds, CancellationToken cancellationToken) =>
        await Context.SaleCancellations.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.ShopId == shopId && saleIds.Contains(x.SaleId))
            .OrderBy(x => x.CancelledAt).ThenBy(x => x.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SaleCancellation>> GetPostedInRangeAsync(Guid shopId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
        await Context.SaleCancellations.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.ShopId == shopId && x.BusinessDate >= fromDate && x.BusinessDate <= toDate)
            .OrderBy(x => x.CancelledAt).ThenBy(x => x.Id).ToListAsync(cancellationToken);
}
