using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class SupplierRepository(ApplicationDbContext context)
    : RepositoryBase<Supplier>(context), ISupplierRepository
{
    public async Task<IReadOnlyList<Supplier>> GetByOwnerUserIdAsync(Guid ownerUserId, bool includeSystem, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(s => s.OwnerUserId == ownerUserId && (includeSystem || !s.IsSystem))
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

    public async Task<Supplier?> GetSystemByOwnerUserIdAsync(Guid ownerUserId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(s => s.OwnerUserId == ownerUserId && s.IsSystem)
            .FirstOrDefaultAsync(cancellationToken);
}