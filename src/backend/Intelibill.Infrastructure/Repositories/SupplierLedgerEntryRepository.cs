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

    public async Task<decimal> GetSupplierBalanceAsync(Guid shopId, Guid supplierId, CancellationToken cancellationToken = default)
    {
        var entries = await DbSet
            .Where(e => e.ShopId == shopId && e.SupplierId == supplierId)
            .Select(e => new { e.EntryType, e.Amount })
            .ToListAsync(cancellationToken);

        return entries.Sum(e => e.EntryType == SupplierLedgerEntryType.PaymentMade ? -e.Amount : e.Amount);
    }

    public async Task<decimal> GetSupplierPayablesAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        var supplierBalances = await DbSet
            .Where(e => e.ShopId == shopId)
            .GroupBy(e => e.SupplierId)
            .Select(group => group.Sum(e => e.EntryType == SupplierLedgerEntryType.PaymentMade ? -e.Amount : e.Amount))
            .ToListAsync(cancellationToken);

        return supplierBalances.Sum(balance => Math.Max(0m, balance));
    }
}
