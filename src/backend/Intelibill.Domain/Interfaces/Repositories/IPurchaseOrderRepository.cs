using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Interfaces.Repositories;

public sealed record PurchaseOrderListFilter(
    Guid ShopId,
    string? Search,
    PurchaseOrderStatus? Status,
    DateOnly? OrderDateFrom,
    DateOnly? OrderDateTo,
    int PageNumber,
    int PageSize);

public interface IPurchaseOrderRepository : IRepository<PurchaseOrder>
{
    Task<PurchaseOrder?> GetDetailAsync(Guid purchaseOrderId, CancellationToken cancellationToken = default);
    Task<PurchaseOrder?> GetByShopAndIdAsync(Guid shopId, Guid purchaseOrderId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<PurchaseOrder> Items, int TotalCount)> GetByShopAsync(
        PurchaseOrderListFilter filter,
        CancellationToken cancellationToken = default);
}
