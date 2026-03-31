using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface ISupplierRepository : IRepository<Supplier>
{
    Task<IReadOnlyList<Supplier>> GetByOwnerUserIdAsync(Guid ownerUserId, CancellationToken cancellationToken = default);
}