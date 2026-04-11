using ErrorOr;
using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.AddInventory;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using DomainInventory = Intelibill.Domain.Entities.Inventory;

namespace Intelibill.Application.Features.Inventory.Commands.AddInventoryBatch;

public sealed class AddInventoryBatchCommandHandler(
    IUserRepository userRepository,
    IItemRepository itemRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IStockTransactionRepository stockTransactionRepository,
    ISupplierLedgerEntryRepository supplierLedgerEntryRepository,
    IInventoryRepository inventoryRepository,
    IUnitOfWork unitOfWork)
{
    private const int MaxBatchSize = 100;
    private readonly AddInventoryCommandValidator _rowValidator = new();

    public async Task<ErrorOr<AddInventoryBatchResultDto>> HandleAsync(AddInventoryBatchCommand command, CancellationToken cancellationToken)
    {
        if (command.Items.Count == 0)
            return Error.Validation("Inventory.BatchEmpty", "At least one inventory row is required.");

        if (command.Items.Count > MaxBatchSize)
            return Error.Validation("Inventory.BatchLimitExceeded", "Only 100 items are allowed in a batch.");

        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Inventory.UserIsNotOwnerOrManager;

        var succeeded = new List<AddInventoryBatchSucceededRowDto>();
        var failed = new List<AddInventoryBatchFailedRowDto>();

        var itemByBarcodeCache = new Dictionary<string, Item>(StringComparer.Ordinal);
        var itemByNameCache = new Dictionary<string, Item>(StringComparer.Ordinal);
        var inventoryCache = new Dictionary<Guid, DomainInventory>();

        foreach (var row in command.Items)
        {
            var rowErrors = ValidateRow(command, row);
            if (rowErrors.Count > 0)
            {
                failed.Add(ToFailedRow(row, rowErrors));
                continue;
            }

            var rowResult = await ProcessRowAsync(
                command,
                row,
                itemByBarcodeCache,
                itemByNameCache,
                inventoryCache,
                cancellationToken);

            if (rowResult.IsError)
            {
                failed.Add(ToFailedRow(row, rowResult.Errors));
                continue;
            }

            succeeded.Add(new AddInventoryBatchSucceededRowDto(row.ClientRowId, rowResult.Value));
        }

        if (succeeded.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new AddInventoryBatchResultDto(
            command.Items.Count,
            succeeded.Count,
            failed.Count,
            succeeded,
            failed);
    }

    private List<Error> ValidateRow(AddInventoryBatchCommand command, AddInventoryBatchRowCommand row)
    {
        var validationTarget = new AddInventoryCommand(
            command.ActorUserId,
            command.ActiveShopId,
            row.ItemName,
            row.Barcode,
            row.ItemDescription,
            row.Uom,
            row.BatchNumber,
            row.Quantity,
            row.CostPrice,
            row.Mrp,
            row.SalesPrice,
            row.TaxRatePercent,
            row.TaxIncluded,
            row.ExpiryDate,
            row.ManufacturingDate,
            row.SupplierId,
            row.ReferenceNumber,
            row.Notes,
            row.PerformedAt);

        var validation = _rowValidator.Validate(validationTarget);
        if (validation.IsValid)
            return [];

        return validation.Errors
            .Select(e => Error.Validation(e.ErrorCode, e.ErrorMessage))
            .ToList();
    }

    private async Task<ErrorOr<AddInventoryResultDto>> ProcessRowAsync(
        AddInventoryBatchCommand command,
        AddInventoryBatchRowCommand row,
        Dictionary<string, Item> itemByBarcodeCache,
        Dictionary<string, Item> itemByNameCache,
        Dictionary<Guid, DomainInventory> inventoryCache,
        CancellationToken cancellationToken)
    {
        var normalizedName = row.ItemName.Trim();
        var normalizedBarcode = row.Barcode.Trim();

        var cacheBarcodeKey = BuildItemCacheKey(command.ActiveShopId, normalizedBarcode);
        var cacheNameKey = BuildItemCacheKey(command.ActiveShopId, normalizedName);

        if (!itemByBarcodeCache.TryGetValue(cacheBarcodeKey, out var itemByBarcode))
        {
            itemByBarcode = await itemRepository.GetByBarcodeAsync(command.ActiveShopId, normalizedBarcode, cancellationToken);
            if (itemByBarcode is not null) itemByBarcodeCache[cacheBarcodeKey] = itemByBarcode;
        }

        if (!itemByNameCache.TryGetValue(cacheNameKey, out var itemByName))
        {
            itemByName = await itemRepository.GetByNameAsync(command.ActiveShopId, normalizedName, cancellationToken);
            if (itemByName is not null) itemByNameCache[cacheNameKey] = itemByName;
        }

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
                row.ItemDescription,
                row.Uom,
                normalizedBarcode,
                isActive: true,
                createdBy: command.ActorUserId);

            await itemRepository.AddAsync(item, cancellationToken);
            itemByBarcodeCache[cacheBarcodeKey] = item;
            itemByNameCache[cacheNameKey] = item;
        }

        var normalizedBatchNumber = row.BatchNumber.Trim();
        var existingBatch = await inventoryBatchRepository.GetByBatchNumberAsync(
            command.ActiveShopId,
            item.Id,
            normalizedBatchNumber,
            cancellationToken);

        if (existingBatch is not null && !existingBatch.IsVoided)
        {
            return Errors.Inventory.BatchNumberAlreadyExists;
        }

        var batchResult = InventoryBatch.Create(
            command.ActiveShopId,
            item.Id,
            normalizedBatchNumber,
            row.Quantity,
            row.CostPrice,
            row.Mrp,
            row.SalesPrice,
            row.TaxRatePercent,
            row.TaxIncluded,
            row.ExpiryDate,
            row.ManufacturingDate,
            row.SupplierId,
            command.ActorUserId);

        if (batchResult.IsError)
            return batchResult.Errors;

        var batch = batchResult.Value;
        await inventoryBatchRepository.AddAsync(batch, cancellationToken);

        var performedAt = row.PerformedAt ?? DateTimeOffset.UtcNow;
        var stockTransactionResult = StockTransaction.Create(
            command.ActiveShopId,
            item.Id,
            batch.Id,
            StockTransactionType.In,
            row.Quantity,
            row.ReferenceNumber,
            row.Notes,
            performedAt,
            command.ActorUserId,
            command.ActorUserId);

        if (stockTransactionResult.IsError)
            return stockTransactionResult.Errors;

        await stockTransactionRepository.AddAsync(stockTransactionResult.Value, cancellationToken);

        if (batch.SupplierId is Guid supplierId)
        {
            var ledgerResult = SupplierLedgerEntry.Create(
                command.ActiveShopId,
                supplierId,
                batch.Id,
                SupplierLedgerEntryType.GoodsReceived,
                ComputeLedgerAmount(row.CostPrice, row.Quantity),
                DateOnly.FromDateTime(performedAt.UtcDateTime),
                row.Notes ?? "Inbound inventory",
                command.ActorUserId);

            if (ledgerResult.IsError)
                return Errors.Inventory.SupplierLedgerEntryInvalid;

            await supplierLedgerEntryRepository.AddAsync(ledgerResult.Value, cancellationToken);
        }

        if (!inventoryCache.TryGetValue(item.Id, out var inventory))
        {
            inventory = await inventoryRepository.GetByItemAsync(command.ActiveShopId, item.Id, cancellationToken);
            if (inventory is not null)
                inventoryCache[item.Id] = inventory;
        }

        if (inventory is null)
        {
            var inventoryResult = DomainInventory.Create(
                command.ActiveShopId,
                item.Id,
                row.Quantity,
                reorderLevel: 0,
                maxLevel: 0,
                createdBy: command.ActorUserId);

            if (inventoryResult.IsError)
                return inventoryResult.Errors;

            inventory = inventoryResult.Value;
            await inventoryRepository.AddAsync(inventory, cancellationToken);
            inventoryCache[item.Id] = inventory;
        }
        else
        {
            var addQuantityResult = inventory.AddQuantity(row.Quantity, command.ActorUserId);
            if (addQuantityResult.IsError)
                return addQuantityResult.Errors;

            inventoryRepository.Update(inventory);
        }

        return new AddInventoryResultDto(
            item.Id,
            item.Name,
            item.Barcode,
            batch.Id,
            batch.BatchNumber,
            batch.Quantity,
            inventory.Quantity,
            batch.SupplierId,
            stockTransactionResult.Value.Id,
            stockTransactionResult.Value.PerformedAt);
    }

    private static AddInventoryBatchFailedRowDto ToFailedRow(AddInventoryBatchRowCommand row, IReadOnlyList<Error> errors) =>
        new(
            row.ClientRowId,
            row.ItemName,
            row.Barcode,
            errors.Select(error => new AddInventoryBatchRowErrorDto(error.Code, error.Description)).ToArray());

    private static string BuildItemCacheKey(Guid shopId, string value) => $"{shopId:N}:{value}";

    private static string BuildBatchCacheKey(Guid shopId, Guid itemId, string batchNumber) => $"{shopId:N}:{itemId:N}:{batchNumber}";

    private static decimal ComputeLedgerAmount(decimal costPrice, decimal quantity) =>
        decimal.Round(costPrice * quantity, 2, MidpointRounding.AwayFromZero);
}