using Intelibill.Domain.Entities;
using Intelibill.Domain.ReadModels;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface ICustomerRepository : IRepository<Customer>
{
    Task<PartyDirectoryPage<Customer>> GetDirectoryAsync(Guid shopId, PartyDirectoryQuery query, CancellationToken cancellationToken = default);
    Task<CustomerDirectorySummary> GetDirectorySummaryAsync(Guid shopId, UtcDateWindow month, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Customer>> GetByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<Customer> StreamActiveByShopAsync(Guid shopId, CancellationToken cancellationToken = default);
    Task<Customer?> GetByShopAndIdAsync(Guid shopId, Guid customerId, CancellationToken cancellationToken = default);
    Task<Customer?> GetByShopAndPhoneAsync(Guid shopId, string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks the customer row (<c>FOR NO KEY UPDATE</c>, 5 s budget) inside the current transaction:
    /// the same first lock a payment or return takes, so credit decisions on one account queue.
    /// The budget applies to this lock only.
    /// </summary>
    Task<RowLockOutcome> LockForCreditAsync(Guid shopId, Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>The customer as committed now, bypassing any copy the context already tracks.</summary>
    Task<Customer?> GetCurrentAsync(Guid shopId, Guid customerId, CancellationToken cancellationToken = default);
}
