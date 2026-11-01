using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Services;

/// <summary>The retail terms and lot details a price edit may change.</summary>
public sealed record BatchRetailTerms(
    string BatchNumber,
    decimal Mrp,
    decimal SalesPrice,
    decimal TaxRatePercent,
    bool TaxIncluded,
    DateOnly? ExpiryDate,
    DateOnly? ManufacturingDate);

/// <param name="PriceRevision">Appended when a price term changed; null for a details-only edit.</param>
public sealed record BatchRetailTermsRevision(BatchPriceRevision? PriceRevision)
{
    public bool PricingChanged => PriceRevision is not null;
}

public interface IBatchRetailTermsReviser
{
    /// <summary>
    /// Applies <paramref name="terms"/> to the batch in place and stages the batch write and any
    /// price revision. The caller authorizes, checks the version and saves.
    /// </summary>
    Task<ErrorOr<BatchRetailTermsRevision>> ReviseAsync(
        InventoryBatch batch,
        BatchRetailTerms terms,
        string? reason,
        Guid actorUserId,
        CancellationToken cancellationToken);
}

internal sealed class BatchRetailTermsReviser(
    IInventoryBatchRepository inventoryBatchRepository,
    IBatchPriceRevisionRepository batchPriceRevisionRepository) : IBatchRetailTermsReviser
{
    public async Task<ErrorOr<BatchRetailTermsRevision>> ReviseAsync(
        InventoryBatch batch,
        BatchRetailTerms terms,
        string? reason,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var batchNumber = terms.BatchNumber.Trim();
        if (!string.Equals(batchNumber, batch.BatchNumber, StringComparison.Ordinal))
        {
            var existing = await inventoryBatchRepository.GetByBatchNumberAsync(batch.ShopId, batch.ItemId, batchNumber, cancellationToken);
            if (existing is not null && existing.Id != batch.Id && !existing.IsVoided)
                return Errors.Inventory.BatchNumberAlreadyExists;
        }

        var previous = BatchPricing.Of(batch);
        var revised = batch.ReviseRetailTerms(
            batchNumber,
            terms.Mrp,
            terms.SalesPrice,
            terms.TaxRatePercent,
            terms.TaxIncluded,
            terms.ExpiryDate,
            terms.ManufacturingDate,
            actorUserId);
        if (revised.IsError)
            return revised.Errors;

        // Written even when nothing changed, so the save is checked against the version the editor
        // was shown and a stale form can never silently pass.
        inventoryBatchRepository.Update(batch);

        if (BatchPricing.Of(batch) == previous)
            return new BatchRetailTermsRevision(null);

        var revision = BatchPriceRevision.Create(batch, previous, reason, DateTimeOffset.UtcNow, actorUserId);
        if (revision.IsError)
            return revision.Errors;

        await batchPriceRevisionRepository.AddAsync(revision.Value, cancellationToken);
        return new BatchRetailTermsRevision(revision.Value);
    }
}
