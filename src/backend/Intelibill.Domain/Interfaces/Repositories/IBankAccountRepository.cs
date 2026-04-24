using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IBankAccountRepository : IRepository<BankAccount>
{
    Task<IReadOnlyList<BankAccount>> GetByOwnerUserIdAsync(Guid ownerUserId, CancellationToken cancellationToken = default);
}
