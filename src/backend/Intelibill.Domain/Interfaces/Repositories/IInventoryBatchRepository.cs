using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IInventoryBatchRepository : IRepository<InventoryBatch>
{
    Task<IReadOnlyList<InventoryBatch>> GetByItemAsync(Guid shopId, Guid itemId, CancellationToken cancellationToken = default);
}