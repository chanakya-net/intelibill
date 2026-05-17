using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IHsnCacheRepository : IRepository<HsnCache>
{
    Task<HsnCache?> GetByProductNameAsync(string productName, CancellationToken cancellationToken = default);
    Task SaveAsync(HsnCache entry, CancellationToken cancellationToken = default);
}
