using Intelibill.Domain.Entities;
using Intelibill.Domain.ReadModels;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface ISupplierRepository : IRepository<Supplier>
{
    Task<PartyDirectoryPage<Supplier>> GetDirectoryAsync(Guid shopId, PartyDirectoryQuery query, bool includeSystem, CancellationToken cancellationToken = default);
    Task<SupplierDirectorySummary> GetDirectorySummaryAsync(Guid shopId, bool includeSystem, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Supplier>> GetByShopIdAsync(Guid shopId, bool includeSystem, CancellationToken cancellationToken = default);
    Task<Supplier?> GetSystemByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default);
}
