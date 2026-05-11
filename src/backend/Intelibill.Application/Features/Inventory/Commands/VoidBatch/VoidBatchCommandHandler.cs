using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Events;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace Intelibill.Application.Features.Inventory.Commands.VoidBatch;

public sealed class VoidBatchCommandHandler(
    IUserRepository userRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IStockTransactionRepository stockTransactionRepository,
    ISupplierLedgerEntryRepository supplierLedgerEntryRepository,
    IInventoryRepository inventoryRepository,
    IUnitOfWork unitOfWork,
    IMessageBus messageBus)
{
    public async Task<ErrorOr<VoidBatchResultDto>> HandleAsync(VoidBatchCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not ShopRole.Owner)
            return Errors.Inventory.UserIsNotOwner;

        var batch = await inventoryBatchRepository.GetByIdAsync(command.BatchId, cancellationToken);
        if (batch is null || batch.ShopId != command.ActiveShopId)
            return Errors.Inventory.BatchNotFound;

        if (batch.IsVoided)
            return Errors.Inventory.BatchAlreadyVoided;

        var remainingQuantity = batch.Quantity;
        var originalQuantity = batch.OriginalQuantity;
        var costPrice = batch.CostPrice;
        var supplierId = batch.SupplierId;
        var itemId = batch.ItemId;

        batch.Void(command.ActorUserId);
        inventoryBatchRepository.Update(batch);

        var now = DateTimeOffset.UtcNow;
        var stockTransactionResult = StockTransaction.Create(
            command.ActiveShopId,
            itemId,
            batch.Id,
            StockTransactionType.Reversal,
            -originalQuantity,
            null,
            "Batch voided by owner",
            now,
            command.ActorUserId,
            command.ActorUserId);

        if (stockTransactionResult.IsError)
            return stockTransactionResult.Errors;

        await stockTransactionRepository.AddAsync(stockTransactionResult.Value, cancellationToken);

        decimal? ledgerReversalAmount = null;
        if (supplierId.HasValue)
        {
            ledgerReversalAmount = -decimal.Round(originalQuantity * costPrice, 2, MidpointRounding.AwayFromZero);
            var ledgerResult = SupplierLedgerEntry.Create(
                command.ActiveShopId,
                supplierId.Value,
                batch.Id,
                SupplierLedgerEntryType.Reversal,
                ledgerReversalAmount.Value,
                DateOnly.FromDateTime(now.UtcDateTime),
                "Batch voided — supplier ledger corrected",
                command.ActorUserId);

            if (ledgerResult.IsError)
                return Errors.Inventory.SupplierLedgerEntryInvalid;

            await supplierLedgerEntryRepository.AddAsync(ledgerResult.Value, cancellationToken);
        }

        var subtractResult = await SubtractInventoryWithRetryAsync(
            command.ActiveShopId,
            itemId,
            remainingQuantity,
            command.ActorUserId,
            cancellationToken);

        if (subtractResult.IsError)
            return subtractResult.Errors;

        await messageBus.PublishAsync(
            new InventoryBatchVoidedDomainEvent(
                command.ActiveShopId,
                itemId,
                batch.Id));

        return new VoidBatchResultDto(
            batch.Id,
            originalQuantity,
            remainingQuantity,
            ledgerReversalAmount);
    }

    private async Task<ErrorOr<Success>> SubtractInventoryWithRetryAsync(
        Guid shopId,
        Guid itemId,
        decimal quantityToSubtract,
        Guid actorUserId,
        CancellationToken ct,
        int maxRetries = 3)
    {
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var inventory = await inventoryRepository.GetByItemAsync(shopId, itemId, ct);
                if (inventory is null)
                    return Errors.Inventory.InventoryAggregateNotFound;

                var subtractResult = inventory.SubtractQuantity(quantityToSubtract, actorUserId);
                if (subtractResult.IsError)
                    return subtractResult.Errors;

                inventoryRepository.Update(inventory);

                await unitOfWork.SaveChangesAsync(ct);
                return Result.Success;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                if (attempt == maxRetries - 1)
                    return Error.Conflict(
                        "Inventory.UpdateConflict",
                        "Inventory aggregate could not be updated after 3 retries due to concurrent modifications.");

                foreach (var entry in ex.Entries)
                    await entry.ReloadAsync(ct);
            }
        }

        return Error.Conflict(
            "Inventory.UpdateConflict",
            "Inventory aggregate could not be updated after max retries.");
    }
}
