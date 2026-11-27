using ErrorOr;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Expiry;

/// <summary>
/// Posts the stock side of disposing quarantined stock: the physical reduction, its damage movement and its loss adjustment.
/// </summary>
public sealed class BatchDisposalPoster(IInventoryRepository inventory, IStockTransactionRepository movements,
    IInventoryAdjustmentRepository adjustments, IInventoryAdjustmentNumberGenerator numbers)
{
    private static Error Refused(string code, string message) => Error.Conflict("Expiry." + code, message);

    /// <summary>
    /// Disposes the quarantined quantity from the already locked batch and the item total, stages the damage
    /// movement and builds (but does not add) the loss adjustment.
    /// </summary>
    internal async Task<ErrorOr<(InventoryAdjustment Adjustment, Guid MovementId)>> StageDisposalAsync(
        Guid shopId, Guid actor, BatchDispositionRequest request, InventoryBatch batch, BatchDispositionChange root,
        DateOnly day, DateTimeOffset now, CancellationToken ct)
    {
        var stock = await inventory.GetByItemAsync(shopId, batch.ItemId, ct);
        if (stock is null) return Refused("InventoryMissing", "Physical inventory aggregate is missing.");
        var before = batch.Quantity;
        var aggregate = stock.Quantity;
        var disposed = batch.DisposeQuarantine(request.Quantity, actor);
        if (disposed.IsError) return disposed.Errors;
        var reduced = stock.SubtractQuantity(request.Quantity, actor);
        if (reduced.IsError) return reduced.Errors;

        var number = numbers.Generate(day);
        var movement = StockTransaction.Create(shopId, batch.ItemId, batch.Id, StockTransactionType.Dmg, -request.Quantity,
            number, request.Reason.Trim(), now, actor, actor);
        if (movement.IsError) return movement.Errors;
        var loss = InventoryAdjustment.Create(shopId, batch.ItemId, batch.Id, number, InventoryAdjustmentDirection.Decrease,
            root.ReasonCode == "Expiry.Expired" ? InventoryAdjustmentReason.Expired : InventoryAdjustmentReason.OtherLoss,
            request.Quantity, batch.CostPrice, decimal.Round(request.Quantity * batch.CostPrice, 2, MidpointRounding.AwayFromZero),
            before, batch.Quantity, aggregate, stock.Quantity, now, actor, request.Reason.Trim(), actor);
        if (loss.IsError) return loss.Errors;
        await movements.AddAsync(movement.Value, ct);
        inventory.Update(stock);
        return (loss.Value, movement.Value.Id);
    }

    /// <summary>Links the loss adjustment to its disposition change and adds it.</summary>
    internal async Task AddLinkedAdjustmentAsync(InventoryAdjustment adjustment, Guid changeId, CancellationToken ct)
    {
        adjustment.LinkDisposition(changeId);
        await adjustments.AddAsync(adjustment, ct);
    }
}
