using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface ISupplierRepository : IRepository<Supplier>
{
    Task<IReadOnlyList<Supplier>> GetByOwnerUserIdAsync(Guid ownerUserId, bool includeSystem, CancellationToken cancellationToken = default);
    Task<Supplier?> GetSystemByOwnerUserIdAsync(Guid ownerUserId, CancellationToken cancellationToken = default);
}