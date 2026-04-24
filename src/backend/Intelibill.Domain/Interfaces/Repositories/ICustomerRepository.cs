using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface ICustomerRepository : IRepository<Customer>
{
    Task<IReadOnlyList<Customer>> GetByOwnerUserIdAsync(Guid ownerUserId, CancellationToken cancellationToken = default);
    Task<Customer?> GetByOwnerAndIdAsync(Guid ownerUserId, Guid customerId, CancellationToken cancellationToken = default);
    Task<Customer?> GetByOwnerAndPhoneAsync(Guid ownerUserId, string phoneNumber, CancellationToken cancellationToken = default);
}
