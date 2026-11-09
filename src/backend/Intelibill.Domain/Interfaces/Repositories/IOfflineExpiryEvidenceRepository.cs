using Intelibill.Domain.Entities;
namespace Intelibill.Domain.Interfaces.Repositories;
public interface IOfflineExpiryEvidenceRepository
{
    Task<OfflineExpiryEvidence?> GetAsync(Guid shopId, Guid evidenceId, CancellationToken ct);
    Task<Guid?> GetBatchItemAsync(Guid shopId, Guid batchId, CancellationToken ct);
    Task AddAsync(OfflineExpiryEvidence evidence, CancellationToken ct);
    Task<IReadOnlyList<InventoryBatch>> GetCapturePageAsync(Guid shopId, Guid? after, int pageSize, CancellationToken ct);
    /// <summary>Every issued catalog version must still have its policy or date revision.</summary>
    Task<bool> HasRetainedCatalogAsync(Guid shopId, long catalogVersion, CancellationToken ct);
    Task<bool> HasRelevantChangeAsync(Guid shopId, Guid itemId, Guid batchId, long catalogVersion, DateTimeOffset asOf, CancellationToken ct);
}
