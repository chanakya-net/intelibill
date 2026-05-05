using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Commands.VoidAdjustment;

public sealed class VoidAdjustmentCommandHandler(
    IUserRepository userRepository,
    IInventoryAdjustmentRepository inventoryAdjustmentRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IInventoryRepository inventoryRepository,
    IStockTransactionRepository stockTransactionRepository,
    IUnitOfWork unitOfWork)
{
    private static readonly VoidAdjustmentCommandValidator Validator = new();

    public async Task<ErrorOr<VoidAdjustmentResultDto>> HandleAsync(
        VoidAdjustmentCommand command,
        CancellationToken cancellationToken)
    {
        var validation = Validator.Validate(command);
        if (!validation.IsValid)
        {
            return validation.Errors
                .Select(e => Error.Validation(e.ErrorCode, e.ErrorMessage))
                .ToList();
        }

        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not ShopRole.Owner)
            return Errors.Inventory.UserIsNotOwner;

        var adjustment = await inventoryAdjustmentRepository.GetByIdAsync(command.AdjustmentId, cancellationToken);
        if (adjustment is null || adjustment.ShopId != command.ActiveShopId)
            return Errors.Inventory.AdjustmentNotFound;

        if (adjustment.IsVoided)
            return Errors.Inventory.AdjustmentAlreadyVoided;

        var batch = await inventoryBatchRepository.GetByIdAsync(adjustment.InventoryBatchId, cancellationToken);
        if (batch is null || batch.ShopId != command.ActiveShopId)
            return Errors.Inventory.BatchNotFound;

        if (batch.IsVoided)
            return Errors.Inventory.BatchAlreadyVoided;

        var inventory = await inventoryRepository.GetByItemAsync(command.ActiveShopId, adjustment.ItemId, cancellationToken);
        if (inventory is null)
            return Errors.Inventory.InventoryAggregateNotFound;

        var batchQuantityBefore = batch.Quantity;
        var inventoryQuantityBefore = inventory.Quantity;
        var stockPrecheck = EnsureReversalWillNotMakeStockNegative(adjustment, batch, inventory);
        if (stockPrecheck.IsError)
            return stockPrecheck.Errors;

        var stockUpdate = ApplyStockReversal(adjustment, batch, inventory, command.ActorUserId);
        if (stockUpdate.IsError)
            return stockUpdate.Errors;

        var voidedAt = DateTimeOffset.UtcNow;
        var reversalQuantity = adjustment.Direction == InventoryAdjustmentDirection.Decrease
            ? adjustment.Quantity
            : -adjustment.Quantity;
        var stockTransaction = StockTransaction.Create(
            command.ActiveShopId,
            adjustment.ItemId,
            adjustment.InventoryBatchId,
            StockTransactionType.Reversal,
            reversalQuantity,
            adjustment.AdjustmentNumber,
            command.Reason,
            voidedAt,
            command.ActorUserId,
            command.ActorUserId);

        if (stockTransaction.IsError)
            return stockTransaction.Errors;

        var voidResult = adjustment.Void(voidedAt, command.ActorUserId, command.Reason, stockTransaction.Value.Id);
        if (voidResult.IsError)
            return voidResult.Errors;

        inventoryAdjustmentRepository.Update(adjustment);
        inventoryBatchRepository.Update(batch);
        inventoryRepository.Update(inventory);
        await stockTransactionRepository.AddAsync(stockTransaction.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new VoidAdjustmentResultDto(
            adjustment.Id,
            stockTransaction.Value.Id,
            batchQuantityBefore,
            batch.Quantity,
            inventoryQuantityBefore,
            inventory.Quantity,
            voidedAt);
    }

    private static ErrorOr<Success> ApplyStockReversal(
        InventoryAdjustment adjustment,
        InventoryBatch batch,
        Intelibill.Domain.Entities.Inventory inventory,
        Guid actorUserId)
    {
        if (adjustment.Direction == InventoryAdjustmentDirection.Decrease)
        {
            var batchUpdate = batch.AddQuantity(adjustment.Quantity, actorUserId);
            if (batchUpdate.IsError)
                return batchUpdate.Errors;

            var inventoryUpdate = inventory.AddQuantity(adjustment.Quantity, actorUserId);
            return inventoryUpdate.IsError ? inventoryUpdate.Errors : Result.Success;
        }

        var subtractBatch = batch.SubtractQuantity(adjustment.Quantity, actorUserId);
        if (subtractBatch.IsError)
            return subtractBatch.Errors;

        var subtractInventory = inventory.SubtractQuantity(adjustment.Quantity, actorUserId);
        return subtractInventory.IsError ? subtractInventory.Errors : Result.Success;
    }

    private static ErrorOr<Success> EnsureReversalWillNotMakeStockNegative(
        InventoryAdjustment adjustment,
        InventoryBatch batch,
        Intelibill.Domain.Entities.Inventory inventory)
    {
        if (adjustment.Direction == InventoryAdjustmentDirection.Decrease)
            return Result.Success;

        if (adjustment.Quantity > batch.Quantity)
            return Error.Validation("InventoryBatch.InsufficientStock", "Insufficient stock in batch.");

        if (adjustment.Quantity > inventory.Quantity)
            return Error.Conflict("Inventory.InsufficientStock", "Not enough stock available for this operation.");

        return Result.Success;
    }
}
