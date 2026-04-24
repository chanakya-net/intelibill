using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface ICustomerLedgerEntryRepository : IRepository<CustomerLedgerEntry>
{
    Task<IReadOnlyList<CustomerLedgerEntry>> GetByCustomerAsync(Guid shopId, Guid customerId, CancellationToken cancellationToken = default);
    Task<decimal> GetCustomerBalanceAsync(Guid shopId, Guid customerId, CancellationToken cancellationToken = default);
}
