using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Expiry;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Inventory.Commands.AddInventory;

/// <summary>Resolves a receipt's item, batch number and supplier before anything is staged.</summary>
public sealed class ReceiptIntakeResolver(
    ISupplierRepository supplierRepository,
    IItemRepository itemRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    BatchExpiryRevisionService expiryDates)
{
    internal sealed record ResolvedItem(Item Item, bool IsNew);

    internal async Task<ErrorOr<ResolvedItem>> ResolveItemAsync(
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

    internal async Task<ErrorOr<Success>> PrepareItemAsync(
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

    internal async Task<ErrorOr<Success>> EnsureBatchNumberFreeAsync(
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

    internal async Task<ErrorOr<Supplier>> ResolveEffectiveSupplierAsync(Guid? requestedSupplierId, Guid shopId, CancellationToken cancellationToken)
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
}
