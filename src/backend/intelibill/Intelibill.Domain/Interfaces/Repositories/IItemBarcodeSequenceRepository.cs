namespace Intelibill.Domain.Interfaces.Repositories;

public interface IItemBarcodeSequenceRepository
{
    Task<string> GetNextCodeAsync(Guid shopId, CancellationToken cancellationToken = default);
}
