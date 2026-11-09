using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class CustomerLedgerEntryRepository(ApplicationDbContext context)
    : RepositoryBase<CustomerLedgerEntry>(context), ICustomerLedgerEntryRepository
{
    public async Task<IReadOnlyList<CustomerLedgerEntry>> GetByCustomerAsync(Guid shopId, Guid customerId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(e => e.ShopId == shopId && e.CustomerId == customerId)
            .OrderByDescending(e => e.EntryDate)
            .ThenByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<decimal> GetCustomerBalanceAsync(Guid shopId, Guid customerId, CancellationToken cancellationToken = default)
    {
        var row = await PartyLedgerQueries.Customers(DbSet.Where(e => e.ShopId == shopId && e.CustomerId == customerId))
            .SingleOrDefaultAsync(cancellationToken);
        PartyLedgerQueries.RejectUnknown(row?.UnknownTypes ?? 0);
        return row?.Amount ?? 0m;
    }

    public async Task<decimal> GetCustomerCreditDueAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        var total = await PartyLedgerQueries.Customers(DbSet.Where(e => e.ShopId == shopId))
            .GroupBy(_ => 1)
            .Select(g => new { Amount = g.Sum(e => e.Amount > 0m ? e.Amount : 0m), Unknown = g.Sum(e => e.UnknownTypes) })
            .SingleOrDefaultAsync(cancellationToken);
        PartyLedgerQueries.RejectUnknown(total?.Unknown ?? 0);
        return total?.Amount ?? 0m;
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetCustomerBalancesAsync(
        Guid shopId, IReadOnlyCollection<Guid> customerIds, CancellationToken cancellationToken = default)
    {
        if (customerIds.Count == 0) return new Dictionary<Guid, decimal>();
        var rows = await PartyLedgerQueries.Customers(DbSet.Where(e => e.ShopId == shopId && customerIds.Contains(e.CustomerId)))
            .ToListAsync(cancellationToken);
        PartyLedgerQueries.RejectUnknown(rows.Sum(r => r.UnknownTypes));
        return rows.ToDictionary(r => r.PartyId, r => r.Amount);
    }
}
