using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface ISupplierRepository : IRepository<Supplier>
{
    Task<IReadOnlyList<Supplier>> GetByShopIdAsync(Guid shopId, bool includeSystem, CancellationToken cancellationToken = default);
    Task<Supplier?> GetSystemByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default);
}
