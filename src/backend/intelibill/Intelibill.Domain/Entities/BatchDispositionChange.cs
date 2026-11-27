using Intelibill.Domain.Common;
using Intelibill.Domain.ValueObjects;
using System.Text.Json;

namespace Intelibill.Domain.Entities;

/// <summary>Immutable source-rooted physical hold; children consume only their own root.</summary>
public sealed class BatchDispositionChange : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid InventoryBatchId { get; private set; }
    public Guid? RootChangeId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public string SourceKind { get; private set; } = string.Empty;
    public Guid SourceId { get; private set; }
    public Guid SourceLineId { get; private set; }
    public Guid? StockMovementId { get; private set; }
    public Guid? InventoryAdjustmentId { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }
    public DateOnly? ManufacturingDate { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public string PolicyJson { get; private set; } = string.Empty;
    public DateOnly BusinessDate { get; private set; }
    public Guid ActorUserId { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    private BatchDispositionChange() { }
    public static BatchDispositionChange Quarantine(InventoryBatch batch, string sourceKind, Guid sourceId, Guid sourceLineId,
        Guid movementId, decimal quantity, string reasonCode, ExpiryPolicy policy, DateOnly date, Guid actor, DateTimeOffset now) => new()
    {
        ShopId = batch.ShopId, ItemId = batch.ItemId, InventoryBatchId = batch.Id, Action = "Quarantine", Quantity = quantity,
        SourceKind = sourceKind, SourceId = sourceId, SourceLineId = sourceLineId, StockMovementId = movementId,
        ExpiryDate = batch.ExpiryDate, ManufacturingDate = batch.ManufacturingDate, ReasonCode = reasonCode, Reason = reasonCode,
        PolicyJson = JsonSerializer.Serialize(policy), BusinessDate = date, ActorUserId = actor, RecordedAt = now,
    };
    public static BatchDispositionChange Consume(BatchDispositionChange root, string action, decimal quantity, string reason,
        Guid? movementId, Guid? adjustmentId, DateOnly date, Guid actor, DateTimeOffset now) => new()
    {
        ShopId = root.ShopId, ItemId = root.ItemId, InventoryBatchId = root.InventoryBatchId, RootChangeId = root.Id,
        Action = action, Quantity = quantity, SourceKind = root.SourceKind, SourceId = root.SourceId, SourceLineId = root.SourceLineId,
        StockMovementId = movementId, InventoryAdjustmentId = adjustmentId, ExpiryDate = root.ExpiryDate,
        ManufacturingDate = root.ManufacturingDate, ReasonCode = root.ReasonCode, PolicyJson = root.PolicyJson,
        Reason = reason, BusinessDate = date, ActorUserId = actor, RecordedAt = now,
    };
}
