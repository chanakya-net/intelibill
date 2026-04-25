using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface ICustomerRepository : IRepository<Customer>
{
    Task<IReadOnlyList<Customer>> GetByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default);
    Task<Customer?> GetByShopAndIdAsync(Guid shopId, Guid customerId, CancellationToken cancellationToken = default);
    Task<Customer?> GetByShopAndPhoneAsync(Guid shopId, string phoneNumber, CancellationToken cancellationToken = default);
}
