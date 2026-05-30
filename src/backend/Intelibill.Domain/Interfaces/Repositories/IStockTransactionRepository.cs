using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IStockTransactionRepository : IRepository<StockTransaction>
{
    Task<IReadOnlyList<StockTransaction>> GetByItemAsync(Guid shopId, Guid itemId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockTransaction>> GetByBatchAsync(Guid shopId, Guid inventoryBatchId, CancellationToken cancellationToken = default);
}
