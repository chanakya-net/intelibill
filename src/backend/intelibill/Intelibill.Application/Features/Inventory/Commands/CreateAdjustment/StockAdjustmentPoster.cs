using ErrorOr;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Commands.CreateAdjustment;

/// <summary>Applies a manual adjustment to its batch and inventory, numbers it, and stages it with its stock movement.</summary>
public sealed class StockAdjustmentPoster(
    IInventoryBatchRepository inventoryBatchRepository,
    IInventoryRepository inventoryRepository,
    IStockTransactionRepository stockTransactionRepository,
    IInventoryAdjustmentRepository inventoryAdjustmentRepository,
    IInventoryAdjustmentNumberGenerator adjustmentNumberGenerator,
    IBusinessClock businessClock)
{
    internal ErrorOr<(StockTransaction StockTransaction, InventoryAdjustment Adjustment)> ApplyAdjustment(
        CreateAdjustmentCommand command,
        Shop shop,
        InventoryBatch batch,
        Intelibill.Domain.Entities.Inventory inventory,
        DateTimeOffset performedAt)
    {
        var batchQuantityBefore = batch.Quantity;
        var inventoryQuantityBefore = inventory.Quantity;

        var batchUpdate = ApplyBatchAdjustment(batch, command);
        if (batchUpdate.IsError)
            return batchUpdate.Errors;

        var inventoryUpdate = ApplyInventoryAdjustment(inventory, command);
        if (inventoryUpdate.IsError)
            return inventoryUpdate.Errors;

        var adjustmentNumber = adjustmentNumberGenerator.Generate(
            businessClock.GetBusinessDate(performedAt, shop.BusinessTimeZoneId));

        var stockTransaction = BuildStockTransaction(command, batch, adjustmentNumber, performedAt);
        if (stockTransaction.IsError)
            return stockTransaction.Errors;

        var adjustment = BuildAdjustment(
            command,
            batch,
            inventory,
            adjustmentNumber,
            batchQuantityBefore,
            inventoryQuantityBefore,
            performedAt);
        if (adjustment.IsError)
            return adjustment.Errors;

        return (stockTransaction.Value, adjustment.Value);
    }

    internal async Task StageAdjustmentAsync(
        InventoryBatch batch,
        Intelibill.Domain.Entities.Inventory inventory,
        StockTransaction stockTransaction,
        InventoryAdjustment adjustment,
        CancellationToken cancellationToken)
    {
        inventoryBatchRepository.Update(batch);
        inventoryRepository.Update(inventory);
        await stockTransactionRepository.AddAsync(stockTransaction, cancellationToken);
        await inventoryAdjustmentRepository.AddAsync(adjustment, cancellationToken);
    }

    private static ErrorOr<StockTransaction> BuildStockTransaction(
        CreateAdjustmentCommand command,
        InventoryBatch batch,
        string adjustmentNumber,
        DateTimeOffset performedAt)
    {
        var signedQuantity = command.Direction == InventoryAdjustmentDirection.Increase
            ? command.Quantity
            : -command.Quantity;

        return StockTransaction.Create(
            command.ActiveShopId,
            batch.ItemId,
            batch.Id,
            MapStockTransactionType(command.Direction, command.Reason),
            signedQuantity,
            adjustmentNumber,
            command.Notes,
            performedAt,
            command.ActorUserId,
            command.ActorUserId);
    }

    private static ErrorOr<InventoryAdjustment> BuildAdjustment(
        CreateAdjustmentCommand command,
        InventoryBatch batch,
        Intelibill.Domain.Entities.Inventory inventory,
        string adjustmentNumber,
        decimal batchQuantityBefore,
        decimal inventoryQuantityBefore,
        DateTimeOffset performedAt)
    {
        var costImpact = decimal.Round(command.Quantity * batch.CostPrice, 2, MidpointRounding.AwayFromZero);

        return InventoryAdjustment.Create(
            command.ActiveShopId,
            batch.ItemId,
            batch.Id,
            adjustmentNumber,
            command.Direction,
            command.Reason,
            command.Quantity,
            batch.CostPrice,
            costImpact,
            batchQuantityBefore,
            batch.Quantity,
            inventoryQuantityBefore,
            inventory.Quantity,
            performedAt,
            command.ActorUserId,
            command.Notes,
            command.ActorUserId);
    }

    private static ErrorOr<Success> ApplyBatchAdjustment(InventoryBatch batch, CreateAdjustmentCommand command)
    {
        return command.Direction == InventoryAdjustmentDirection.Increase
            ? batch.AddQuantity(command.Quantity, command.ActorUserId)
            : batch.SubtractQuantity(command.Quantity, command.ActorUserId);
    }

    private static ErrorOr<Success> ApplyInventoryAdjustment(Intelibill.Domain.Entities.Inventory inventory, CreateAdjustmentCommand command)
    {
        return command.Direction == InventoryAdjustmentDirection.Increase
            ? inventory.AddQuantity(command.Quantity, command.ActorUserId)
            : inventory.SubtractQuantity(command.Quantity, command.ActorUserId);
    }

    private static StockTransactionType MapStockTransactionType(
        InventoryAdjustmentDirection direction,
        InventoryAdjustmentReason reason)
    {
        if (direction == InventoryAdjustmentDirection.Increase)
            return StockTransactionType.In;

        return reason switch
        {
            InventoryAdjustmentReason.Damaged or InventoryAdjustmentReason.Expired => StockTransactionType.Dmg,
            InventoryAdjustmentReason.Stolen => StockTransactionType.Stol,
            _ => StockTransactionType.Out,
        };
    }
}
