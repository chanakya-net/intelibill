using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface ICustomerRepository : IRepository<Customer>
{
    Task<IReadOnlyList<Customer>> GetByOwnerUserIdAsync(Guid ownerUserId, CancellationToken cancellationToken = default);
}
