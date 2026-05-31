using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IItemBarcodeSequenceRepository
{
    Task<ItemBarcodeSequence?> GetByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default);
    Task AddAsync(ItemBarcodeSequence sequence, CancellationToken cancellationToken = default);
}
