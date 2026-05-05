using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Commands.CreateAdjustment;

public sealed class CreateAdjustmentCommandHandler(
    IUserRepository userRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IInventoryRepository inventoryRepository,
    IStockTransactionRepository stockTransactionRepository,
    IInventoryAdjustmentRepository inventoryAdjustmentRepository,
    IInventoryAdjustmentNumberGenerator adjustmentNumberGenerator,
    IUnitOfWork unitOfWork)
{
    private static readonly CreateAdjustmentCommandValidator Validator = new();

    public async Task<ErrorOr<InventoryAdjustmentResultDto>> HandleAsync(
        CreateAdjustmentCommand command,
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

        if (actorMembership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Inventory.UserIsNotOwnerOrManager;

        var batch = await inventoryBatchRepository.GetByIdAsync(command.BatchId, cancellationToken);
        if (batch is null || batch.ShopId != command.ActiveShopId)
            return Errors.Inventory.BatchNotFound;

        if (batch.IsVoided)
            return Errors.Inventory.BatchAlreadyVoided;

        var inventory = await inventoryRepository.GetByItemAsync(command.ActiveShopId, batch.ItemId, cancellationToken);
        if (inventory is null)
            return Errors.Inventory.InventoryAggregateNotFound;

        if (command.Direction == InventoryAdjustmentDirection.Decrease && command.Quantity > batch.Quantity)
            return Errors.Inventory.AdjustmentQuantityExceedsBatchQuantity;

        var performedAt = command.PerformedAt ?? DateTimeOffset.UtcNow;
        var batchQuantityBefore = batch.Quantity;
        var inventoryQuantityBefore = inventory.Quantity;

        var batchUpdate = ApplyBatchAdjustment(batch, command);
        if (batchUpdate.IsError)
            return batchUpdate.Errors;

        var inventoryUpdate = ApplyInventoryAdjustment(inventory, command);
        if (inventoryUpdate.IsError)
            return inventoryUpdate.Errors;

        var adjustmentNumber = adjustmentNumberGenerator.Generate(performedAt);
        var signedQuantity = command.Direction == InventoryAdjustmentDirection.Increase
            ? command.Quantity
            : -command.Quantity;

        var stockTransaction = StockTransaction.Create(
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

        if (stockTransaction.IsError)
            return stockTransaction.Errors;

        var costImpact = decimal.Round(command.Quantity * batch.CostPrice, 2, MidpointRounding.AwayFromZero);
        var adjustment = InventoryAdjustment.Create(
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

        if (adjustment.IsError)
            return adjustment.Errors;

        inventoryBatchRepository.Update(batch);
        inventoryRepository.Update(inventory);
        await stockTransactionRepository.AddAsync(stockTransaction.Value, cancellationToken);
        await inventoryAdjustmentRepository.AddAsync(adjustment.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new InventoryAdjustmentResultDto(
            adjustment.Value.Id,
            adjustment.Value.AdjustmentNumber,
            batch.Id,
            adjustment.Value.Quantity,
            adjustment.Value.UnitCost,
            adjustment.Value.CostImpact,
            adjustment.Value.BatchQuantityBefore,
            adjustment.Value.BatchQuantityAfter,
            adjustment.Value.InventoryQuantityBefore,
            adjustment.Value.InventoryQuantityAfter,
            stockTransaction.Value.Id,
            adjustment.Value.PerformedAt);
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
