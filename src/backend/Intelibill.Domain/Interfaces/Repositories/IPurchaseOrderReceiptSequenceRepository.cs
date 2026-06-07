using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IPurchaseOrderReceiptSequenceRepository : IRepository<PurchaseOrderReceiptSequence>
{
    Task<PurchaseOrderReceiptSequence> GetOrCreateByShopAndYearAsync(
        Guid shopId,
        int year,
        CancellationToken cancellationToken = default);
}
