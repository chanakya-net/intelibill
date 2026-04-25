using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IBankAccountRepository : IRepository<BankAccount>
{
    Task<IReadOnlyList<BankAccount>> GetByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default);
}
