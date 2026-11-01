using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IPurchaseReceiptValueRepository : IRepository<PurchaseReceiptValue>
{
    Task<PurchaseReceiptValue?> GetByBatchAsync(Guid shopId, Guid inventoryBatchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PurchaseReceiptValue>> GetByShopAsync(Guid shopId, CancellationToken cancellationToken = default);
}
