using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Expiry;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using DomainInventory = Intelibill.Domain.Entities.Inventory;

namespace Intelibill.Application.Features.Inventory.Commands.AddInventory;

public sealed class AddInventoryCommandHandler(
    IUserRepository userRepository,
    ISupplierRepository supplierRepository,
    IItemRepository itemRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IStockTransactionRepository stockTransactionRepository,
    ISupplierLedgerEntryRepository supplierLedgerEntryRepository,
    IPurchaseReceiptValueRepository purchaseReceiptValueRepository,
    IInventoryRepository inventoryRepository,
    IUnitOfWork unitOfWork,
    IShopRepository shopRepository,
    IPostingPeriodPolicy postingPeriodPolicy,
    IBusinessClock businessClock, IExpiryPolicyFence expiryFence, BatchExpiryRevisionService expiryDates,
    StockIntakeAccess access)
{
    [LicenceAction(LicenceAction.NewTrading)]
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

        var shop = await shopRepository.GetByIdAsync(command.ActiveShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var performedAt = (command.PerformedAt ?? businessClock.UtcNow).ToUniversalTime();
        return await PostingTransaction.RunAsync(
            unitOfWork,
            ct => PostAsync(command, shop, performedAt, ct),
            cancellationToken);
    }

    private async Task<ErrorOr<AddInventoryResultDto>> PostAsync(
        AddInventoryCommand command,
        Shop shop,
        DateTimeOffset performedAt,
        CancellationToken cancellationToken)
    {
        var licence = await access.EnterAsync(command.ActorUserId, command.ActiveShopId, command.Session, cancellationToken);
        if (licence.IsError)
            return licence.Errors;

        // The payable is dated by the business day of the receipt's performed time.
        var fence = await postingPeriodPolicy.AuthorizeForWriteAsync(
            command.ActiveShopId,
            businessClock.GetBusinessDate(performedAt, shop.BusinessTimeZoneId),
            cancellationToken);
        if (fence.IsError)
            return fence.Errors;

        var expiryLock = await expiryFence.AcquireAsync(command.ActiveShopId, false, cancellationToken);
        if (expiryLock.IsError) return expiryLock.Errors;
        if (licence.Value.CurrentDecision().EnforcedDenial() is { } denied)
            return denied;

        return await StageReceiptAsync(command, shop, performedAt, licence.Value, cancellationToken);
    }

    private sealed record ResolvedItem(Item Item, bool IsNew);

    private async Task<ErrorOr<AddInventoryResultDto>> StageReceiptAsync(
        AddInventoryCommand command,
        Shop shop,
        DateTimeOffset performedAt,
        ShopLicenceLease licence,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveItemAsync(command, cancellationToken);
        if (resolved.IsError)
            return resolved.Errors;

        var item = resolved.Value.Item;

        var prepared = await PrepareItemAsync(command, resolved.Value, cancellationToken);
        if (prepared.IsError)
            return prepared.Errors;

        var batchNumberCheck = await EnsureBatchNumberFreeAsync(command, item, cancellationToken);
        if (batchNumberCheck.IsError)
            return batchNumberCheck.Errors;

        var effectiveSupplierOrError = await ResolveEffectiveSupplierAsync(
            command.SupplierId,
            command.ActiveShopId,
            cancellationToken);
        if (effectiveSupplierOrError.IsError)
            return effectiveSupplierOrError.Errors;

        var effectiveSupplier = effectiveSupplierOrError.Value;

        var batchResult = await StageBatchAsync(command, item, effectiveSupplier, cancellationToken);
        if (batchResult.IsError)
            return batchResult.Errors;

        var batch = batchResult.Value;

        var stockTransactionResult = await StageStockTransactionAsync(
            command,
            item,
            batch,
            performedAt,
            cancellationToken);
        if (stockTransactionResult.IsError)
            return stockTransactionResult.Errors;

        var stockTransaction = stockTransactionResult.Value;

        var ledgerAmount = ComputeLedgerAmount(command.TotalPurchaseCost);

        var receiptValue = await StageReceiptValueAsync(
            command,
            item,
            batch,
            effectiveSupplier,
            ledgerAmount,
            performedAt,
            cancellationToken);
        if (receiptValue.IsError)
            return receiptValue.Errors;

        var ledger = await StageSupplierLedgerEntryAsync(
            command,
            shop,
            batch,
            effectiveSupplier,
            ledgerAmount,
            performedAt,
            cancellationToken);
        if (ledger.IsError)
            return ledger.Errors;

        var inventoryResult = await UpsertInventoryWithRetryAsync(
            command, item.Id, licence, cancellationToken);

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

    private async Task<ErrorOr<ResolvedItem>> ResolveItemAsync(
        AddInventoryCommand command,
        CancellationToken cancellationToken)
    {
        var normalizedName = command.ItemName.Trim();
        var normalizedBarcode = command.Barcode.Trim();

        var itemByBarcode = await itemRepository.GetByBarcodeAsync(
            command.ActiveShopId,
            normalizedBarcode,
            cancellationToken);
        var itemByName = await itemRepository.GetByNameAsync(
            command.ActiveShopId,
            normalizedName,
            cancellationToken);

        if (itemByBarcode is not null && itemByName is not null && itemByBarcode.Id != itemByName.Id)
            return Errors.Inventory.ItemIdentityConflict;

        if (itemByBarcode is not null)
        {
            if (!string.Equals(itemByBarcode.Name, normalizedName, StringComparison.Ordinal))
                return Errors.Inventory.ItemNameBarcodeMismatch;

            return new ResolvedItem(itemByBarcode, IsNew: false);
        }

        if (itemByName is not null)
        {
            if (!string.Equals(itemByName.Barcode, normalizedBarcode, StringComparison.Ordinal))
                return Errors.Inventory.ItemNameBarcodeMismatch;

            return new ResolvedItem(itemByName, IsNew: false);
        }

        var newItem = Item.Create(
            command.ActiveShopId,
            normalizedName,
            command.ItemDescription,
            command.Uom,
            normalizedBarcode,
            isActive: true,
            createdBy: command.ActorUserId);

        return new ResolvedItem(newItem, IsNew: true);
    }

    private async Task<ErrorOr<Success>> PrepareItemAsync(
        AddInventoryCommand command,
        ResolvedItem resolved,
        CancellationToken cancellationToken)
    {
        var item = resolved.Item;
        var isNewItem = resolved.IsNew;

        var quantityCheck = QuantityPolicy.Validate(command.Quantity, item.QuantityScale, item.Uom);
        if (quantityCheck.IsError)
        {
            return Errors.Inventory.FromQuantityCheck(quantityCheck.FirstError, item.Uom, item.QuantityScale);
        }

        if (isNewItem)
            await itemRepository.AddAsync(item, cancellationToken);

        var dates = await expiryDates.ValidateReceiptAsync(
            command.ActiveShopId,
            item.Id,
            command.ExpiryDate,
            command.ManufacturingDate,
            cancellationToken);
        if (dates.IsError)
            return dates.Errors;

        var hsnCode = string.IsNullOrWhiteSpace(command.HsnCode) ? item.HsnCode : command.HsnCode;
        item.UpdateTaxDefaults(hsnCode, command.TaxRatePercent, command.TaxIncluded);
        if (!isNewItem)
            itemRepository.Update(item);

        return Result.Success;
    }

    private async Task<ErrorOr<Success>> EnsureBatchNumberFreeAsync(
        AddInventoryCommand command,
        Item item,
        CancellationToken cancellationToken)
    {
        var existingBatch = await inventoryBatchRepository.GetByBatchNumberAsync(
            command.ActiveShopId,
            item.Id,
            command.BatchNumber,
            cancellationToken);

        if (existingBatch is not null && !existingBatch.IsVoided)
        {
            return Errors.Inventory.BatchNumberAlreadyExists;
        }

        return Result.Success;
    }

    private async Task<ErrorOr<InventoryBatch>> StageBatchAsync(
        AddInventoryCommand command,
        Item item,
        Supplier effectiveSupplier,
        CancellationToken cancellationToken)
    {
        var batchResult = InventoryBatch.Create(
            command.ActiveShopId,
            item.Id,
            command.BatchNumber,
            command.Quantity,
            ComputeUnitCost(command.TotalPurchaseCost, command.Quantity),
            command.Mrp,
            command.SalesPrice,
            command.TaxRatePercent,
            command.TaxIncluded,
            command.ExpiryDate,
            command.ManufacturingDate,
            effectiveSupplier.Id,
            command.ActorUserId,
            command.PurchaseTaxIncluded);

        if (batchResult.IsError)
            return batchResult.Errors;

        var batch = batchResult.Value;
        await inventoryBatchRepository.AddAsync(batch, cancellationToken);

        return batch;
    }

    private async Task<ErrorOr<StockTransaction>> StageStockTransactionAsync(
        AddInventoryCommand command,
        Item item,
        InventoryBatch batch,
        DateTimeOffset performedAt,
        CancellationToken cancellationToken)
    {
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

        return stockTransaction;
    }

    private async Task<ErrorOr<Success>> StageReceiptValueAsync(
        AddInventoryCommand command,
        Item item,
        InventoryBatch batch,
        Supplier effectiveSupplier,
        decimal ledgerAmount,
        DateTimeOffset performedAt,
        CancellationToken cancellationToken)
    {
        // The receipt's total as posted, kept so a later correction or void works from it rather
        // than from the rounded unit cost.
        var receiptValueResult = PurchaseReceiptValue.Create(
            command.ActiveShopId,
            batch.Id,
            item.Id,
            effectiveSupplier.Id,
            command.Quantity,
            ledgerAmount,
            command.ReferenceNumber,
            performedAt,
            command.ActorUserId);

        if (receiptValueResult.IsError)
            return receiptValueResult.Errors;

        await purchaseReceiptValueRepository.AddAsync(receiptValueResult.Value, cancellationToken);

        return Result.Success;
    }

    private async Task<ErrorOr<Success>> StageSupplierLedgerEntryAsync(
        AddInventoryCommand command,
        Shop shop,
        InventoryBatch batch,
        Supplier effectiveSupplier,
        decimal ledgerAmount,
        DateTimeOffset performedAt,
        CancellationToken cancellationToken)
    {
        var isSystemSupplier = command.SupplierId is null;

        // A free receipt (sample, bonus stock) owes the supplier nothing: no zero-value payable.
        if (ledgerAmount != 0)
        {
            var ledgerResult = SupplierLedgerEntry.Create(
                command.ActiveShopId,
                effectiveSupplier.Id,
                batch.Id,
                SupplierLedgerEntryType.GoodsReceived,
                ledgerAmount,
                businessClock.GetBusinessDate(performedAt, shop.BusinessTimeZoneId),
                isSystemSupplier ? "Receipt with no supplier assigned" : null,
                command.ActorUserId);

            if (ledgerResult.IsError)
                return Errors.Inventory.SupplierLedgerEntryInvalid;

            await supplierLedgerEntryRepository.AddAsync(ledgerResult.Value, cancellationToken);
        }

        return Result.Success;
    }

    private async Task<ErrorOr<DomainInventory>> UpsertInventoryWithRetryAsync(
        AddInventoryCommand command,
        Guid itemId,
        ShopLicenceLease licence,
        CancellationToken ct,
        int maxRetries = 3)
    {
        var (shopId, createdBy, quantityToAdd) = (command.ActiveShopId, command.ActorUserId, command.Quantity);
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

                if (await access.CheckWriteAsync(createdBy, shopId, command.Session, licence, ct) is { } beforeSave)
                    return beforeSave;

                await unitOfWork.SaveChangesAsync(ct);
                if (await access.CheckWriteAsync(createdBy, shopId, command.Session, licence, ct) is { } beforeCommit)
                    return beforeCommit;

                return inventory;
            }
            catch (Exception ex) when (unitOfWork.IsConcurrencyConflict(ex))
            {
                if (attempt == maxRetries - 1)
                    return Error.Conflict(
                        "Inventory.UpdateConflict",
                        "Inventory aggregate could not be updated after 3 retries due to concurrent modifications.");

                await unitOfWork.ReloadConflictedEntitiesAsync(ex, ct);
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

    private static decimal ComputeUnitCost(decimal totalPurchaseCost, decimal quantity) =>
        decimal.Round(totalPurchaseCost / quantity, 2, MidpointRounding.AwayFromZero);

    private static decimal ComputeLedgerAmount(decimal totalPurchaseCost) =>
        decimal.Round(totalPurchaseCost, 2, MidpointRounding.AwayFromZero);
}
