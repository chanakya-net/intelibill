using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;
using DomainInventory = Intelibill.Domain.Entities.Inventory;

namespace Intelibill.Application.Features.Inventory.Commands.AddInventory;

public sealed class AddInventoryCommandHandler(
    IUserRepository userRepository,
    ISupplierRepository supplierRepository,
    IItemRepository itemRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IStockTransactionRepository stockTransactionRepository,
    ISupplierLedgerEntryRepository supplierLedgerEntryRepository,
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
        var isNewItem = false;
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
            isNewItem = true;
        }

        var hsnCode = string.IsNullOrWhiteSpace(command.HsnCode) ? item.HsnCode : command.HsnCode;
        item.UpdateTaxDefaults(hsnCode, command.TaxRatePercent, command.TaxIncluded);
        if (!isNewItem)
            itemRepository.Update(item);

        var existingBatch = await inventoryBatchRepository.GetByBatchNumberAsync(
            command.ActiveShopId,
            item.Id,
            command.BatchNumber,
            cancellationToken);

        if (existingBatch is not null && !existingBatch.IsVoided)
        {
            return Errors.Inventory.BatchNumberAlreadyExists;
        }

        var effectiveSupplierOrError = await ResolveEffectiveSupplierAsync(command.SupplierId, command.ActiveShopId, cancellationToken);
        if (effectiveSupplierOrError.IsError)
            return effectiveSupplierOrError.Errors;

        var effectiveSupplier = effectiveSupplierOrError.Value;
        var isSystemSupplier = command.SupplierId is null;

        var batchResult = InventoryBatch.Create(
            command.ActiveShopId,
            item.Id,
            command.BatchNumber,
            command.Quantity,
            command.CostPrice,
            command.Mrp,
            command.SalesPrice,
            command.TaxRatePercent,
            command.TaxIncluded,
            command.ExpiryDate,
            command.ManufacturingDate,
            effectiveSupplier.Id,
            command.ActorUserId);

        if (batchResult.IsError)
            return batchResult.Errors;

        var batch = batchResult.Value;
        await inventoryBatchRepository.AddAsync(batch, cancellationToken);

        var performedAt = (command.PerformedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
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

        var ledgerResult = SupplierLedgerEntry.Create(
            command.ActiveShopId,
            effectiveSupplier.Id,
            batch.Id,
            SupplierLedgerEntryType.GoodsReceived,
            ComputeLedgerAmount(command.CostPrice, command.Quantity),
            DateOnly.FromDateTime(performedAt.UtcDateTime),
            isSystemSupplier ? "Receipt with no supplier assigned" : null,
            command.ActorUserId);

        if (ledgerResult.IsError)
            return Errors.Inventory.SupplierLedgerEntryInvalid;

        await supplierLedgerEntryRepository.AddAsync(ledgerResult.Value, cancellationToken);

        var inventoryResult = await UpsertInventoryWithRetryAsync(
            command.ActiveShopId,
            item.Id,
            command.Quantity,
            command.ActorUserId,
            cancellationToken);

        if (inventoryResult.IsError)
            return inventoryResult.Errors;

        var inventory = inventoryResult.Value;

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

    private async Task<ErrorOr<DomainInventory>> UpsertInventoryWithRetryAsync(
        Guid shopId,
        Guid itemId,
        decimal quantityToAdd,
        Guid createdBy,
        CancellationToken ct,
        int maxRetries = 3)
    {
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var inventory = await inventoryRepository.GetByItemAsync(shopId, itemId, ct);

                if (inventory is null)
                {
                    var createResult = DomainInventory.Create(shopId, itemId, quantityToAdd, reorderLevel: 0, maxLevel: 0, createdBy);
                    if (createResult.IsError)
                        return createResult.Errors;

                    inventory = createResult.Value;
                    await inventoryRepository.AddAsync(inventory, ct);
                }
                else
                {
                    var addResult = inventory.AddQuantity(quantityToAdd, createdBy);
                    if (addResult.IsError)
                        return addResult.Errors;

                    inventoryRepository.Update(inventory);
                }

                await unitOfWork.SaveChangesAsync(ct);
                return inventory;
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

    private async Task<ErrorOr<Supplier>> ResolveEffectiveSupplierAsync(Guid? requestedSupplierId, Guid shopId, CancellationToken cancellationToken)
    {
        if (requestedSupplierId is Guid supplierId)
        {
            var supplier = await supplierRepository.GetByIdAsync(supplierId, cancellationToken);
            if (supplier is null || supplier.ShopId != shopId)
            {
                return Errors.Supplier.SupplierNotFound;
            }

            return supplier;
        }

        var systemSupplier = await supplierRepository.GetSystemByShopIdAsync(shopId, cancellationToken);
        if (systemSupplier is null)
        {
            return Errors.Supplier.SystemSupplierNotFound;
        }

        return systemSupplier;
    }

    private static decimal ComputeLedgerAmount(decimal costPrice, decimal quantity) =>
        decimal.Round(costPrice * quantity, 2, MidpointRounding.AwayFromZero);
}
