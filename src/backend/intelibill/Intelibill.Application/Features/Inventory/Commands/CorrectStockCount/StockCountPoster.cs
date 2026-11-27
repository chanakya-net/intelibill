using ErrorOr;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Commands.CorrectStockCount;

/// <summary>Applies a count's delta as one signed movement and adjustment, and stages the count's writes.</summary>
public sealed class StockCountPoster(
    IInventoryBatchRepository inventoryBatchRepository,
    IInventoryRepository inventoryRepository,
    IStockTransactionRepository stockTransactionRepository,
    IInventoryAdjustmentRepository inventoryAdjustmentRepository,
    IStockCountRepository stockCountRepository,
    IInventoryAdjustmentNumberGenerator adjustmentNumberGenerator)
{
    internal async Task StageCountAsync(
        CorrectStockCountCommand command,
        InventoryBatch batch,
        Domain.Entities.Inventory inventory,
        InventoryAdjustment? adjustment,
        StockTransaction? movement,
        StockCount count,
        CancellationToken cancellationToken)
    {
        batch.MarkUpdatedBy(command.ActorUserId);
        inventoryBatchRepository.Update(batch);
        if (movement is not null)
        {
            inventoryRepository.Update(inventory);
            await stockTransactionRepository.AddAsync(movement, cancellationToken);
            await inventoryAdjustmentRepository.AddAsync(adjustment!, cancellationToken);
        }

        await stockCountRepository.AddAsync(count, cancellationToken);
    }

    internal ErrorOr<(InventoryAdjustment Adjustment, StockTransaction Movement)> PostDelta(
        InventoryBatch batch,
        Domain.Entities.Inventory inventory,
        CorrectStockCountCommand command,
        decimal delta,
        DateTimeOffset performedAt,
        DateOnly businessDate)
    {
        var quantity = Math.Abs(delta);
        var direction = delta > 0 ? InventoryAdjustmentDirection.Increase : InventoryAdjustmentDirection.Decrease;
        var batchQuantityBefore = batch.Quantity;
        var inventoryQuantityBefore = inventory.Quantity;

        var batchUpdate = delta > 0
            ? batch.AddQuantity(quantity, command.ActorUserId)
            : batch.SubtractQuantity(quantity, command.ActorUserId);
        if (batchUpdate.IsError)
            return batchUpdate.Errors;

        var inventoryUpdate = delta > 0
            ? inventory.AddQuantity(quantity, command.ActorUserId)
            : inventory.SubtractQuantity(quantity, command.ActorUserId);
        if (inventoryUpdate.IsError)
            return inventoryUpdate.Errors;

        var adjustmentNumber = adjustmentNumberGenerator.Generate(businessDate);
        var reason = command.Reason.Trim();

        var movement = StockTransaction.Create(
            command.ActiveShopId,
            batch.ItemId,
            batch.Id,
            delta > 0 ? StockTransactionType.In : StockTransactionType.Out,
            delta,
            adjustmentNumber,
            reason,
            performedAt,
            command.ActorUserId,
            command.ActorUserId);
        if (movement.IsError)
            return movement.Errors;

        var adjustment = InventoryAdjustment.Create(
            command.ActiveShopId,
            batch.ItemId,
            batch.Id,
            adjustmentNumber,
            direction,
            InventoryAdjustmentReason.StockCountCorrection,
            quantity,
            batch.CostPrice,
            decimal.Round(quantity * batch.CostPrice, 2, MidpointRounding.AwayFromZero),
            batchQuantityBefore,
            batch.Quantity,
            inventoryQuantityBefore,
            inventory.Quantity,
            performedAt,
            command.ActorUserId,
            reason,
            command.ActorUserId);
        if (adjustment.IsError)
            return adjustment.Errors;

        return (adjustment.Value, movement.Value);
    }
}
