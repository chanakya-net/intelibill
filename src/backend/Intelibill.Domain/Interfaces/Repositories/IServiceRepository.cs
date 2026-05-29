using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IServiceRepository : IRepository<Service>
{
    Task<Service?> GetByCodeAsync(Guid shopId, string code, CancellationToken cancellationToken = default);
    Task<Service?> GetByNameAsync(Guid shopId, string name, CancellationToken cancellationToken = default);
    Task<string> GetNextCodeAsync(Guid shopId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Service>> GetByShopIdAsync(Guid shopId, bool includeInactive, string? search = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Service>> SearchActiveAsync(Guid shopId, string searchTerm, CancellationToken cancellationToken = default);
}
