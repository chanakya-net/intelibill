using Intelibill.Domain.Common;
using Intelibill.Domain.ValueObjects;
namespace Intelibill.Domain.Entities;

/// <summary>Immutable server-issued authority, independent of the device's copy and mutable stock.</summary>
public sealed class OfflineExpiryEvidence : BaseEntity
{
    public Guid SnapshotId { get; private set; }
    public Guid ShopId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid BatchId { get; private set; }
    public DateTimeOffset CapturedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public long CatalogVersion { get; private set; }
    public ExpiryPolicy EffectivePolicy { get; private set; } = null!;
    public ExpiryBatchFacts BatchFacts { get; private set; } = null!;
    private OfflineExpiryEvidence() { }
    public static OfflineExpiryEvidence Capture(Guid snapshotId, InventoryBatch batch, ExpiryPolicy policy, long catalogVersion, DateTimeOffset at) => new()
    {
        SnapshotId = snapshotId, ShopId = batch.ShopId, ItemId = batch.ItemId, BatchId = batch.Id,
        CapturedAt = at, ExpiresAt = at.AddDays(7), CatalogVersion = catalogVersion, EffectivePolicy = policy,
        BatchFacts = new(batch.Id, batch.ItemId, batch.ExpiryDate, batch.ManufacturingDate, batch.Quantity,
            batch.QuarantinedQuantity, batch.IsVoided, batch.Item.IsActive, batch.Version),
    };
}
