using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class SupplierRepository(ApplicationDbContext context)
    : RepositoryBase<Supplier>(context), ISupplierRepository
{
    public async Task<IReadOnlyList<Supplier>> GetByOwnerUserIdAsync(Guid ownerUserId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(s => s.OwnerUserId == ownerUserId)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
}