using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class BankAccountRepository(ApplicationDbContext context)
    : RepositoryBase<BankAccount>(context), IBankAccountRepository
{
    public async Task<IReadOnlyList<BankAccount>> GetByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(ba => ba.ShopId == shopId)
            .OrderBy(ba => ba.BankName)
            .ToListAsync(cancellationToken);
}
