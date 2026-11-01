using ErrorOr;
using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

/// <summary>
/// One change of a batch's retail pricing: the MRP, sale price and tax terms before and after.
/// </summary>
/// <remarks>
/// Applies to future sales only. Sales already made keep the prices and cost they were snapshotted
/// with, and the receipt payable is unaffected.
/// </remarks>
public sealed class BatchPriceRevision : BaseEntity
{
    public const int MaxReasonLength = 500;

    public Guid ShopId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid InventoryBatchId { get; private set; }
    public decimal PreviousMrp { get; private set; }
    public decimal NewMrp { get; private set; }
    public decimal PreviousSalesPrice { get; private set; }
    public decimal NewSalesPrice { get; private set; }
    public decimal PreviousTaxRatePercent { get; private set; }
    public decimal NewTaxRatePercent { get; private set; }
    public bool PreviousTaxIncluded { get; private set; }
    public bool NewTaxIncluded { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset PerformedAt { get; private set; }
    public Guid PerformedBy { get; private set; }

    private BatchPriceRevision() { }

    public static ErrorOr<BatchPriceRevision> Create(
        InventoryBatch batch,
        BatchPricing previous,
        string? reason,
        DateTimeOffset performedAt,
        Guid performedBy)
    {
        var current = BatchPricing.Of(batch);
        if (current == previous)
            return Error.Validation("BatchPriceRevision.Unchanged", "A price revision must change at least one price.");

        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (normalizedReason is { Length: > MaxReasonLength })
            return Error.Validation("BatchPriceRevision.ReasonTooLong", $"Reason cannot exceed {MaxReasonLength} characters.");

        return new BatchPriceRevision
        {
            ShopId = batch.ShopId,
            ItemId = batch.ItemId,
            InventoryBatchId = batch.Id,
            PreviousMrp = previous.Mrp,
            NewMrp = current.Mrp,
            PreviousSalesPrice = previous.SalesPrice,
            NewSalesPrice = current.SalesPrice,
            PreviousTaxRatePercent = previous.TaxRatePercent,
            NewTaxRatePercent = current.TaxRatePercent,
            PreviousTaxIncluded = previous.TaxIncluded,
            NewTaxIncluded = current.TaxIncluded,
            Reason = normalizedReason,
            PerformedAt = performedAt,
            PerformedBy = performedBy,
        };
    }
}

/// <summary>The retail pricing terms of a batch at one moment.</summary>
public sealed record BatchPricing(decimal Mrp, decimal SalesPrice, decimal TaxRatePercent, bool TaxIncluded)
{
    public static BatchPricing Of(InventoryBatch batch) =>
        new(batch.Mrp, batch.SalesPrice, batch.TaxRatePercent, batch.TaxIncluded);
}
