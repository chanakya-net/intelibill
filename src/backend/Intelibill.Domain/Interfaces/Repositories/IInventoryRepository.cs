using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IInventoryRepository : IRepository<Inventory>
{
    Task<Inventory?> GetByItemAsync(Guid shopId, Guid itemId, CancellationToken cancellationToken = default);
}