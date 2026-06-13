using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class CreditNoteRepository(ApplicationDbContext context)
    : RepositoryBase<CreditNote>(context), ICreditNoteRepository
{
    public async Task<CreditNote?> GetByCodeAsync(Guid shopId, string code, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(c => c.Redemptions)
            .FirstOrDefaultAsync(c => c.ShopId == shopId && c.Code == code.Trim(), cancellationToken);

    public async Task<IReadOnlyList<CreditNote>> GetByReturnIdAsync(Guid shopId, Guid saleReturnId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(c => c.ShopId == shopId && c.SaleReturnId == saleReturnId)
            .ToListAsync(cancellationToken);

    public async Task<CreditNote?> GetByIdWithRedemptionsAsync(Guid shopId, Guid id, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(c => c.Redemptions)
            .FirstOrDefaultAsync(c => c.ShopId == shopId && c.Id == id, cancellationToken);
}
