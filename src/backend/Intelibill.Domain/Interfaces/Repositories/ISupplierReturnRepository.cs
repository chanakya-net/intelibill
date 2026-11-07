using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface ISupplierReturnRepository : IRepository<SupplierReturn>
{
    Task<SupplierReturn?> GetDetailAsync(Guid shopId, Guid returnId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SupplierReturn>> GetByReceiptsAsync(Guid shopId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    Task<IReadOnlyList<SupplierReturn>> ListAsync(Guid shopId, Guid? supplierId, Guid? receiptId, string? status, int page, int pageSize, CancellationToken cancellationToken);
    Task AddCreditAsync(SupplierReturnCredit credit, CancellationToken cancellationToken);
    Task<IReadOnlyList<PurchaseReceiptValue>> GetReceiptsAsync(Guid shopId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    Task<IReadOnlyList<PurchaseReceiptValue>> GetGroupReceiptsAsync(Guid shopId, Guid groupId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PurchaseReceiptValue>> GetByPoLineIdsAsync(Guid shopId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    Task<bool> HasCountHistoryAsync(Guid shopId, Guid batchId, CancellationToken cancellationToken);
}
