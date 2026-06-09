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
        var entries = await DbSet
            .Where(e => e.ShopId == shopId && e.CustomerId == customerId)
            .Select(e => new { e.EntryType, e.Amount })
            .ToListAsync(cancellationToken);

        return entries.Sum(e => e.EntryType.ToBalanceDelta(e.Amount));
    }

    public async Task<decimal> GetCustomerCreditDueAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        var balances = await DbSet
            .AsNoTracking()
            .Where(e => e.ShopId == shopId)
            .Select(e => new { e.CustomerId, e.EntryType, e.Amount })
            .ToListAsync(cancellationToken);

        return balances
            .GroupBy(e => e.CustomerId)
            .Select(g => g.Sum(e => e.EntryType.ToBalanceDelta(e.Amount)))
            .Where(balance => balance > 0m)
            .Sum();
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetCustomerBalancesAsync(
        Guid shopId,
        IReadOnlyCollection<Guid> customerIds,
        CancellationToken cancellationToken = default)
    {
        if (customerIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var entries = await DbSet
            .Where(e => e.ShopId == shopId && customerIds.Contains(e.CustomerId))
            .Select(e => new { e.CustomerId, e.EntryType, e.Amount })
            .ToListAsync(cancellationToken);

        return entries
            .GroupBy(e => e.CustomerId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(e => e.EntryType.ToBalanceDelta(e.Amount)));
    }
}
