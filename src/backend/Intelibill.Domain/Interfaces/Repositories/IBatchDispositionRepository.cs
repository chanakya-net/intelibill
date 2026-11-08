using Intelibill.Domain.Entities;
namespace Intelibill.Domain.Interfaces.Repositories;
public interface IBatchDispositionRepository
{
    Task<InventoryBatch?> LockBatchAsync(Guid shopId, Guid batchId, CancellationToken ct);
    Task<BatchDispositionChange?> GetRootAsync(Guid shopId, Guid batchId, Guid rootId, CancellationToken ct);
    Task<decimal> GetRemainingAsync(Guid shopId, Guid rootId, CancellationToken ct);
    Task<bool> HasDateCorrectionAsync(Guid shopId, Guid batchId, DateOnly expiryDate, DateTimeOffset after, CancellationToken ct);
    Task<bool> HasHistoryAsync(Guid shopId, Guid batchId, CancellationToken ct);
    Task<IReadOnlyList<BatchDispositionChange>> GetHistoryAsync(Guid shopId, Guid batchId, CancellationToken ct);
    Task AddAsync(BatchDispositionChange change, CancellationToken ct);
}
