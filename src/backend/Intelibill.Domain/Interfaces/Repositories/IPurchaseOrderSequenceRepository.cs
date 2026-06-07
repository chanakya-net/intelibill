using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IPurchaseOrderSequenceRepository : IRepository<PurchaseOrderSequence>
{
    Task<PurchaseOrderSequence?> GetByShopAndYearAsync(Guid shopId, int year, CancellationToken cancellationToken = default);
    Task<PurchaseOrderSequence> GetOrCreateByShopAndYearAsync(Guid shopId, int year, CancellationToken cancellationToken = default);
}
