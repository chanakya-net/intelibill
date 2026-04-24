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

        return entries.Sum(e => e.EntryType == CustomerLedgerEntryType.PaymentReceived ? -e.Amount : e.Amount);
    }
}
