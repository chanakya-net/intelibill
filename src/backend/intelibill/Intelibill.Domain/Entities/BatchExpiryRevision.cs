using Intelibill.Domain.Common;
namespace Intelibill.Domain.Entities;

public sealed class BatchExpiryRevision : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid InventoryBatchId { get; private set; }
    public DateOnly? PreviousExpiryDate { get; private set; }
    public DateOnly? NewExpiryDate { get; private set; }
    public DateOnly? PreviousManufacturingDate { get; private set; }
    public DateOnly? NewManufacturingDate { get; private set; }
    public uint PreviousBatchVersion { get; private set; }
    public long CatalogVersion { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset EffectiveAt { get; private set; }
    private BatchExpiryRevision() { }
    public static BatchExpiryRevision Create(InventoryBatch batch, DateOnly? expiry, DateOnly? manufacturing, Guid actor, string reason, DateTimeOffset at, long catalogVersion) => new()
    {
        ShopId = batch.ShopId,
        ItemId = batch.ItemId,
        InventoryBatchId = batch.Id,
        PreviousExpiryDate = batch.ExpiryDate,
        PreviousManufacturingDate = batch.ManufacturingDate,
        NewExpiryDate = expiry,
        NewManufacturingDate = manufacturing,
        PreviousBatchVersion = batch.Version,
        CatalogVersion = catalogVersion,
        ActorUserId = actor,
        Reason = reason.Trim(),
        EffectiveAt = at,
    };
}
