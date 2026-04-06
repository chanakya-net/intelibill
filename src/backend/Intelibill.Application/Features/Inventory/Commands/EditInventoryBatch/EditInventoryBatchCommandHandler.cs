using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Commands.EditInventoryBatch;

public sealed class EditInventoryBatchCommandHandler(
    IUserRepository userRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IInventoryRepository inventoryRepository,
    ISupplierLedgerEntryRepository supplierLedgerEntryRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<EditInventoryBatchResultDto>> HandleAsync(EditInventoryBatchCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Inventory.UserIsNotOwnerOrManager;

        var batch = await inventoryBatchRepository.GetByIdAsync(command.InventoryBatchId, cancellationToken);
        if (batch is null || batch.ShopId != command.ActiveShopId)
            return Errors.Inventory.BatchNotFound;

        var previousQuantity = batch.Quantity;
        var previousCostPrice = batch.CostPrice;
        var previousSupplierId = batch.SupplierId;

        var updateResult = batch.Update(
            command.BatchNumber,
            command.Quantity,
            command.CostPrice,
            command.Mrp,
            command.SalesPrice,
            command.TaxRatePercent,
            command.TaxIncluded,
            command.ExpiryDate,
            command.ManufacturingDate,
            command.SupplierId,
            command.ActorUserId);

        if (updateResult.IsError)
            return updateResult.Errors;

        inventoryBatchRepository.Update(batch);

        var inventory = await inventoryRepository.GetByItemAsync(command.ActiveShopId, batch.ItemId, cancellationToken);
        if (inventory is null)
            return Errors.Inventory.InventoryAggregateNotFound;

        var quantityDelta = command.Quantity - previousQuantity;
        if (quantityDelta != 0)
        {
            var inventoryUpdateResult = inventory.UpdateLevels(
                inventory.Quantity + quantityDelta,
                inventory.ReorderLevel,
                inventory.MaxLevel,
                command.ActorUserId);

            if (inventoryUpdateResult.IsError)
                return inventoryUpdateResult.Errors;

            inventoryRepository.Update(inventory);
        }

        if (ShouldCreateCorrectionEntries(previousQuantity, previousCostPrice, previousSupplierId, batch))
        {
            var entryDate = command.EntryDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

            if (previousSupplierId.HasValue)
            {
                var reversalResult = SupplierLedgerEntry.Create(
                    command.ActiveShopId,
                    previousSupplierId.Value,
                    batchId: null,
                    SupplierLedgerEntryType.RecordAdjusted,
                    -ComputeLedgerAmount(previousCostPrice, previousQuantity),
                    entryDate,
                    command.Notes,
                    command.ActorUserId);

                if (reversalResult.IsError)
                    return Errors.Inventory.SupplierLedgerEntryInvalid;

                await supplierLedgerEntryRepository.AddAsync(reversalResult.Value, cancellationToken);
            }

            if (batch.SupplierId.HasValue)
            {
                var correctedResult = SupplierLedgerEntry.Create(
                    command.ActiveShopId,
                    batch.SupplierId.Value,
                    batch.Id,
                    SupplierLedgerEntryType.GoodsReceived,
                    ComputeLedgerAmount(batch.CostPrice, batch.Quantity),
                    entryDate,
                    command.Notes,
                    command.ActorUserId);

                if (correctedResult.IsError)
                    return Errors.Inventory.SupplierLedgerEntryInvalid;

                await supplierLedgerEntryRepository.AddAsync(correctedResult.Value, cancellationToken);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new EditInventoryBatchResultDto(
            batch.Id,
            batch.BatchNumber,
            batch.Quantity,
            batch.CostPrice,
            batch.Mrp,
            batch.SalesPrice,
            batch.TaxRatePercent,
            batch.TaxIncluded,
            batch.SupplierId);
    }

    private static bool ShouldCreateCorrectionEntries(decimal previousQuantity, decimal previousCostPrice, Guid? previousSupplierId, InventoryBatch updatedBatch) =>
        previousQuantity != updatedBatch.Quantity
        || previousCostPrice != updatedBatch.CostPrice
        || previousSupplierId != updatedBatch.SupplierId;

    private static decimal ComputeLedgerAmount(decimal costPrice, decimal quantity) =>
        decimal.Round(costPrice * quantity, 2, MidpointRounding.AwayFromZero);
}
