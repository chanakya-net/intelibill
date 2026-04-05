using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using DomainInventory = Intelibill.Domain.Entities.Inventory;

namespace Intelibill.Application.Features.Inventory.Commands.AddInventory;

public sealed class AddInventoryCommandHandler(
    IUserRepository userRepository,
    IItemRepository itemRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IStockTransactionRepository stockTransactionRepository,
    IInventoryRepository inventoryRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<AddInventoryResultDto>> HandleAsync(AddInventoryCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Inventory.UserIsNotOwnerOrManager;

        var normalizedName = command.ItemName.Trim();
        var normalizedBarcode = command.Barcode.Trim();

        var itemByBarcode = await itemRepository.GetByBarcodeAsync(command.ActiveShopId, normalizedBarcode, cancellationToken);
        var itemByName = await itemRepository.GetByNameAsync(command.ActiveShopId, normalizedName, cancellationToken);

        if (itemByBarcode is not null && itemByName is not null && itemByBarcode.Id != itemByName.Id)
            return Errors.Inventory.ItemIdentityConflict;

        Item item;
        if (itemByBarcode is not null)
        {
            if (!string.Equals(itemByBarcode.Name, normalizedName, StringComparison.Ordinal))
                return Errors.Inventory.ItemNameBarcodeMismatch;

            item = itemByBarcode;
        }
        else if (itemByName is not null)
        {
            if (!string.Equals(itemByName.Barcode, normalizedBarcode, StringComparison.Ordinal))
                return Errors.Inventory.ItemNameBarcodeMismatch;

            item = itemByName;
        }
        else
        {
            item = Item.Create(
                command.ActiveShopId,
                normalizedName,
                command.ItemDescription,
                command.Uom,
                normalizedBarcode,
                isActive: true,
                createdBy: command.ActorUserId);

            await itemRepository.AddAsync(item, cancellationToken);
        }

        var batch = await inventoryBatchRepository.GetByBatchNumberAsync(
            command.ActiveShopId,
            item.Id,
            command.BatchNumber,
            cancellationToken);

        if (batch is null)
        {
            var batchResult = InventoryBatch.Create(
                command.ActiveShopId,
                item.Id,
                command.BatchNumber,
                command.Quantity,
                command.CostPrice,
                command.Mrp,
                command.SalesPrice,
                command.MinSalePrice,
                command.TaxRatePercent,
                command.ExpiryDate,
                command.ManufacturingDate,
                command.SupplierId,
                command.ActorUserId);

            if (batchResult.IsError)
                return batchResult.Errors;

            batch = batchResult.Value;
            await inventoryBatchRepository.AddAsync(batch, cancellationToken);
        }
        else
        {
            var addQuantityResult = batch.AddQuantity(command.Quantity, command.ActorUserId);
            if (addQuantityResult.IsError)
                return addQuantityResult.Errors;

            inventoryBatchRepository.Update(batch);
        }

        var performedAt = command.PerformedAt ?? DateTimeOffset.UtcNow;
        var stockTransactionResult = StockTransaction.Create(
            command.ActiveShopId,
            item.Id,
            batch.Id,
            StockTransactionType.In,
            command.Quantity,
            command.ReferenceNumber,
            command.Notes,
            performedAt,
            command.ActorUserId,
            command.ActorUserId);

        if (stockTransactionResult.IsError)
            return stockTransactionResult.Errors;

        var stockTransaction = stockTransactionResult.Value;
        await stockTransactionRepository.AddAsync(stockTransaction, cancellationToken);

        var inventory = await inventoryRepository.GetByItemAsync(command.ActiveShopId, item.Id, cancellationToken);
        if (inventory is null)
        {
            var inventoryResult = DomainInventory.Create(
                command.ActiveShopId,
                item.Id,
                command.Quantity,
                reorderLevel: 0,
                maxLevel: 0,
                createdBy: command.ActorUserId);

            if (inventoryResult.IsError)
                return inventoryResult.Errors;

            inventory = inventoryResult.Value;
            await inventoryRepository.AddAsync(inventory, cancellationToken);
        }
        else
        {
            var addQuantityResult = inventory.AddQuantity(command.Quantity, command.ActorUserId);
            if (addQuantityResult.IsError)
                return addQuantityResult.Errors;

            inventoryRepository.Update(inventory);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AddInventoryResultDto(
            item.Id,
            item.Name,
            item.Barcode,
            batch.Id,
            batch.BatchNumber,
            batch.Quantity,
            inventory.Quantity,
            batch.SupplierId,
            stockTransaction.Id,
            stockTransaction.PerformedAt);
    }
}
