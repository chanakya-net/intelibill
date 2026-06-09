using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface ICustomerLedgerEntryRepository : IRepository<CustomerLedgerEntry>
{
    Task<IReadOnlyList<CustomerLedgerEntry>> GetByCustomerAsync(Guid shopId, Guid customerId, CancellationToken cancellationToken = default);
    Task<decimal> GetCustomerBalanceAsync(Guid shopId, Guid customerId, CancellationToken cancellationToken = default);
    Task<decimal> GetCustomerCreditDueAsync(Guid shopId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, decimal>> GetCustomerBalancesAsync(
        Guid shopId,
        IReadOnlyCollection<Guid> customerIds,
        CancellationToken cancellationToken = default);
}
