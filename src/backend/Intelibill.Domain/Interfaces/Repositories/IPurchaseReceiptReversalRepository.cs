using Intelibill.Domain.Entities;
namespace Intelibill.Domain.Interfaces.Repositories;
public interface IPurchaseReceiptReversalRepository : IRepository<PurchaseReceiptReversal>
{
    Task<IReadOnlyList<PurchaseReceiptReversal>> GetByReceiptsAsync(Guid shopId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}
