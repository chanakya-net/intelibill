using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class SupplierLedgerEntryRepository(ApplicationDbContext context)
    : RepositoryBase<SupplierLedgerEntry>(context), ISupplierLedgerEntryRepository
{
    public async Task<IReadOnlyList<SupplierLedgerEntry>> GetBySupplierAsync(Guid shopId, Guid supplierId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(e => e.ShopId == shopId && e.SupplierId == supplierId)
            .OrderByDescending(e => e.EntryDate)
            .ThenByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SupplierLedgerEntry>> GetByBatchAsync(Guid shopId, Guid batchId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(e => e.ShopId == shopId && e.BatchId == batchId)
            .OrderByDescending(e => e.EntryDate)
            .ThenByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
}
