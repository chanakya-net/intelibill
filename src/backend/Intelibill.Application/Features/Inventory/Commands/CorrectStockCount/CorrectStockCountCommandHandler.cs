using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Application.Features.Inventory.Commands.CorrectStockCount;

/// <summary>
/// Applies a physical count to a batch as one atomic, attributable correction.
/// </summary>
/// <remarks>
/// <para>
/// The delta is computed from the quantity observed at <c>ExpectedVersion</c>, never taken from the
/// client. A nonzero delta writes exactly one signed movement (<c>In +d</c> or <c>Out -d</c>) and one
/// <see cref="InventoryAdjustment"/>; every count writes a <see cref="StockCount"/>. All of it commits
/// in one save or not at all.
/// </para>
/// <para>
/// The batch row is always written, even for an equal count, so the save is checked against the
/// version the count was taken at: a count racing any other change of the batch is refused with
/// <c>Inventory.BatchVersionConflict</c> and is never reapplied to the newer quantity.
/// </para>
/// </remarks>
public sealed class CorrectStockCountCommandHandler(
    IUserRepository userRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IInventoryRepository inventoryRepository,
    IStockTransactionRepository stockTransactionRepository,
    IInventoryAdjustmentRepository inventoryAdjustmentRepository,
    IStockCountRepository stockCountRepository,
    IInventoryAdjustmentNumberGenerator adjustmentNumberGenerator,
    IUnitOfWork unitOfWork,
    IFinancialOperationExecutor operationExecutor,
    IItemRepository itemRepository,
    IShopRepository shopRepository,
    IPostingPeriodPolicy postingPeriodPolicy,
    IBusinessClock businessClock)
{
    private static readonly CorrectStockCountCommandValidator Validator = new();

    public async Task<ErrorOr<StockCountResultDto>> HandleAsync(
        CorrectStockCountCommand command,
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

        var operationKey = FinancialOperationHasher.NormalizeKey(command.OperationKey);
        if (operationKey is null)
            return await PostingTransaction.RunAsync(unitOfWork, ct => CountAsync(batch, command, ct), cancellationToken);

        var requestHash = FinancialOperationHasher.Compute(new StockCountPayload(
            command.BatchId,
            command.ExpectedVersion,
            FinancialOperationHasher.NormalizeAmount(command.CountedQuantity),
            command.Reason.Trim()));

        return await operationExecutor.ExecuteAsync(
            new FinancialOperationScope(command.ActiveShopId, FinancialOperationType.StockCount, operationKey),
            command.ActorUserId,
            command.BatchId,
            requestHash,
            ct => CountAsync(batch, command, ct),
            cancellationToken);
    }

    private async Task<ErrorOr<StockCountResultDto>> CountAsync(
        InventoryBatch batch,
        CorrectStockCountCommand command,
        CancellationToken cancellationToken)
    {
        var shop = await shopRepository.GetByIdAsync(command.ActiveShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var fence = await postingPeriodPolicy.AuthorizeForWriteAsync(
            command.ActiveShopId,
            businessClock.Today(shop.BusinessTimeZoneId),
            cancellationToken);
        if (fence.IsError)
            return fence.Errors;

        if (batch.IsVoided)
            return Errors.Inventory.BatchAlreadyVoided;

        if (batch.Version != command.ExpectedVersion)
            return Errors.Inventory.BatchVersionConflict;

        var items = await itemRepository.GetByIdsAsync(command.ActiveShopId, [batch.ItemId], cancellationToken);
        var item = items.Count > 0 ? items[0] : null;
        if (item is null)
            return Errors.Inventory.ItemNotFound;

        if (!QuantityPolicy.FitsScale(command.CountedQuantity, item.QuantityScale))
            return Errors.Inventory.QuantityPrecisionExceeded(item.Uom, item.QuantityScale);

        var inventory = await inventoryRepository.GetByItemAsync(command.ActiveShopId, batch.ItemId, cancellationToken);
        if (inventory is null)
            return Errors.Inventory.InventoryAggregateNotFound;

        var performedAt = businessClock.UtcNow;
        var businessDate = businessClock.GetBusinessDate(performedAt, shop.BusinessTimeZoneId);
        var observedQuantity = batch.Quantity;
        var delta = command.CountedQuantity - observedQuantity;

        InventoryAdjustment? adjustment = null;
        StockTransaction? movement = null;
        if (delta != 0)
        {
            var posted = PostDelta(batch, inventory, command, delta, performedAt, businessDate);
            if (posted.IsError)
                return posted.Errors;

            (adjustment, movement) = posted.Value;
        }

        var count = StockCount.Create(
            command.ActiveShopId,
            batch.ItemId,
            batch.Id,
            command.ExpectedVersion,
            observedQuantity,
            command.CountedQuantity,
            command.Reason,
            performedAt,
            command.ActorUserId,
            adjustment?.Id,
            movement?.Id);

        if (count.IsError)
            return count.Errors;

        batch.MarkUpdatedBy(command.ActorUserId);
        inventoryBatchRepository.Update(batch);
        if (movement is not null)
        {
            inventoryRepository.Update(inventory);
            await stockTransactionRepository.AddAsync(movement, cancellationToken);
            await inventoryAdjustmentRepository.AddAsync(adjustment!, cancellationToken);
        }

        await stockCountRepository.AddAsync(count.Value, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Errors.Inventory.BatchVersionConflict;
        }

        return new StockCountResultDto(
            count.Value.Id,
            batch.Id,
            observedQuantity,
            command.CountedQuantity,
            delta,
            adjustment?.Id,
            adjustment?.AdjustmentNumber,
            movement?.Id,
            batch.Quantity,
            inventory.Quantity,
            batch.Version,
            performedAt);
    }

    private ErrorOr<(InventoryAdjustment Adjustment, StockTransaction Movement)> PostDelta(
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

    private sealed record StockCountPayload(Guid BatchId, uint ExpectedVersion, decimal CountedQuantity, string Reason);
}
