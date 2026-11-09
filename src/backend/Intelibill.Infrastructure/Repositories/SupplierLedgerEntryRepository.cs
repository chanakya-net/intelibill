using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
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

    public async Task<decimal> GetSupplierBalanceAsync(Guid shopId, Guid supplierId, CancellationToken cancellationToken = default) =>
        await DbSet.Where(e => e.ShopId == shopId && e.SupplierId == supplierId)
            .SumAsync(e => e.EntryType == SupplierLedgerEntryType.PaymentMade ? -e.Amount : e.Amount, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetSupplierBalancesAsync(
        Guid shopId, IReadOnlyCollection<Guid> supplierIds, CancellationToken cancellationToken = default)
    {
        if (supplierIds.Count == 0) return new Dictionary<Guid, decimal>();
        return await PartyLedgerQueries.Suppliers(DbSet.Where(e => e.ShopId == shopId && supplierIds.Contains(e.SupplierId)))
            .ToDictionaryAsync(e => e.PartyId, e => e.Amount, cancellationToken);
    }

    public async Task<decimal> GetSupplierPayablesAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        await PartyLedgerQueries.Suppliers(DbSet.Where(e => e.ShopId == shopId))
            .Where(e => e.Amount > 0m).SumAsync(e => e.Amount, cancellationToken);
}
