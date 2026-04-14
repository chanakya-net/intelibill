using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface ICustomerRepository : IRepository<Customer>
{
    Task<IReadOnlyList<Customer>> GetByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default);
}
