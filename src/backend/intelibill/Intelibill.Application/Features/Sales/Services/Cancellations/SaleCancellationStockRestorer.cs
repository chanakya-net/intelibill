using ErrorOr;
using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Services.Pricing;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Services.Cancellations;

/// <summary>
/// Puts a cancelled sale's stock back into its original batches and decides how much of it is restored
/// physically or held in quarantine; batch locks are taken here, in the order the method names state.
/// </summary>
public sealed class SaleCancellationStockRestorer(
    IInventoryBatchRepository batches,
    IInventoryRepository inventory,
    IStockTransactionRepository movements,
    BatchDispositionService dispositions)
{
    internal async Task<ErrorOr<List<SaleCancellationPreviewLineDto>>> ReadPreviewLinesAsync(Guid shopId, DateOnly date,
        IReadOnlyList<SaleCancellationPreviewLineDto> lines, CancellationToken ct)
    {
        var preview = new List<SaleCancellationPreviewLineDto>();
        foreach (var line in lines)
        {
            var batch = line.InventoryBatchId is { } id ? await batches.GetByIdAsync(id, ct) : null;
            if (batch is null || batch.ShopId != shopId) { preview.Add(line);
            continue; }
            var disposition = await dispositions.EvaluateAsync(batch, date, line.Quantity, ct);
            if (disposition.IsError) return disposition.Errors;
            preview.Add(line with { PhysicalRestorationQuantity = disposition.Value.PhysicalQuantity,
                QuarantinedQuantity = disposition.Value.QuarantinedQuantity, DispositionReasonCode = disposition.Value.ReasonCode });
        }
        return preview;
    }

    /// <summary>
    /// Locks the batches whose restoration will be quarantined (one call), then restores every line's stock
    /// and stages its cancellation movement.
    /// </summary>
    internal async Task<ErrorOr<StockRestoration>> LockHeldBatchesAndStageStockAsync(Sale sale, Guid actorUserId, string reason,
        BusinessDay day, DateTimeOffset now, SaleCancellationEvaluation e, CancellationToken token)
    {
        var shopId = sale.ShopId;
        List<SaleCancellationLineInput> lines = [];
        List<(InventoryBatch Batch, Guid SaleItemId, StockTransaction Movement)> restorations = [];
        var heldBatchIds = new List<Guid>();
        foreach (var line in sale.Items)
        {
            var originalBatch = await batches.GetByIdAsync(line.InventoryBatchId!.Value, token);
            var restored = await dispositions.EvaluateAsync(originalBatch!, day.Date, line.Quantity, token);
            if (restored.IsError) return restored.Errors;
            if (restored.Value.QuarantinedQuantity > 0) heldBatchIds.Add(originalBatch!.Id);
        }
        await dispositions.LockBatchesAsync(shopId, heldBatchIds, token);
        foreach (var line in sale.Items.OrderBy(x => x.ItemId).ThenBy(x => x.InventoryBatchId).ThenBy(x => x.Id))
        {
            var batch = await batches.GetByIdAsync(line.InventoryBatchId!.Value, token);
            var stock = await inventory.GetByItemAsync(shopId, line.ItemId!.Value, token);
            var added = stock!.AddQuantity(line.Quantity, actorUserId);
            if (added.IsError) return added.Errors;
            var movement = StockTransaction.Create(
                shopId,
                line.ItemId.Value,
                batch!.Id,
                StockTransactionType.SaleCancellation,
                line.Quantity,
                sale.InvoiceNumber,
                reason,
                now,
                actorUserId,
                actorUserId);
            if (movement.IsError) return movement.Errors;
            await movements.AddAsync(movement.Value, token);
            restorations.Add((batch!, line.Id, movement.Value));
            inventory.Update(stock);
            var frozen = e.Lines.Single(x => x.SaleItemId == line.Id);
            lines.Add(
                new(
                    shopId,
                    sale.Id,
                    line.Id,
                    line.ItemId.Value,
                    batch.Id,
                    movement.Value.Id,
                    line.Quantity,
                    frozen.Description,
                    frozen.Uqc,
                    line.CostPrice,
                    line.TotalAmount,
                    SaleEconomicsCalculator.RecordedCost(line.Quantity, line.CostPrice)));
        }
        return new StockRestoration(lines, restorations);
    }

    /// <summary>
    /// Re-decides each restored line's disposition and stages it, locking each batch again when its restoration
    /// is quarantined, then freezes the outcome on the recorded cancellation line.
    /// </summary>
    internal async Task<ErrorOr<Success>> LockQuarantinedBatchesAndStageDispositionsAsync(Guid shopId, Guid actorUserId,
        StockRestoration restoration, SaleCancellation fact, BusinessDay day, DateTimeOffset now, CancellationToken token)
    {
        foreach (var stock in restoration.Movements)
        {
            var recoveredLine = fact.Lines.Single(x => x.SaleItemId == stock.SaleItemId);
            var disposition = await dispositions.EvaluateAsync(stock.Batch, day.Date, recoveredLine.Quantity, token);
            if (disposition.IsError) return disposition.Errors;
            var staged = await dispositions.StageRestorationAsync(shopId, actorUserId, stock.Batch, "SaleCancellation",
                fact.Id, recoveredLine.Id, stock.Movement.Id, disposition.Value, day.Date, now, token);
            if (staged.IsError) return staged.Errors;
            recoveredLine.FreezeDisposition(staged.Value, disposition.Value.QuarantinedQuantity, disposition.Value.ReasonCode);
        }
        return Result.Success;
    }

    internal sealed record StockRestoration(IReadOnlyList<SaleCancellationLineInput> Lines,
        IReadOnlyList<(InventoryBatch Batch, Guid SaleItemId, StockTransaction Movement)> Movements);
}
