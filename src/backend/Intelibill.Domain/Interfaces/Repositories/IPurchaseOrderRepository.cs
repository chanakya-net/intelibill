using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IPurchaseOrderRepository : IRepository<PurchaseOrder>
{
    Task<PurchaseOrder?> GetDetailAsync(Guid purchaseOrderId, CancellationToken cancellationToken = default);
    Task<PurchaseOrder?> GetByShopAndIdAsync(Guid shopId, Guid purchaseOrderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PurchaseOrder>> GetByShopAsync(Guid shopId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<int> CountByShopAsync(Guid shopId, CancellationToken cancellationToken = default);
}
