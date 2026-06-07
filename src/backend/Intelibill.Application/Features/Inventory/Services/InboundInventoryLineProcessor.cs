using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Services;

internal sealed class InboundInventoryLineProcessor(
    IItemResolver itemResolver,
    IItemRepository itemRepository,
    ISupplierResolver supplierResolver,
    IBatchFactory batchFactory,
    IInventoryUpdater inventoryUpdater) : IInboundInventoryLineProcessor
{
    public async Task<ErrorOr<InboundInventoryLineResult>> ProcessAsync(
        Guid shopId,
        InboundInventoryLineInput input,
        Guid actorUserId,
        ItemResolutionContext itemResolutionContext,
        InventoryUpdateContext inventoryUpdateContext,
        CancellationToken cancellationToken)
    {
        var itemOrError = await ResolveItemAsync(shopId, input, actorUserId, itemResolutionContext, cancellationToken);
        if (itemOrError.IsError)
            return itemOrError.Errors;

        var item = itemOrError.Value;

        var supplierOrError = await supplierResolver.ResolveAsync(shopId, input.SupplierId, cancellationToken);
        if (supplierOrError.IsError)
            return supplierOrError.Errors;

        var batchOrError = await batchFactory.CreateBatchAsync(
            shopId,
            item.Id,
            input,
            supplierOrError.Value,
            actorUserId,
            cancellationToken);

        if (batchOrError.IsError)
            return batchOrError.Errors;

        var inventoryOrError = await inventoryUpdater.GetOrUpdateAsync(
            shopId,
            item.Id,
            input.Quantity,
            actorUserId,
            inventoryUpdateContext,
            cancellationToken);

        if (inventoryOrError.IsError)
            return inventoryOrError.Errors;

        var hsnCode = string.IsNullOrWhiteSpace(input.HsnCode) ? item.HsnCode : input.HsnCode;
        item.UpdateTaxDefaults(hsnCode, input.TaxRatePercent, input.TaxIncluded);

        if (input.ItemId is null)
        {
            var existingItem = await itemRepository.GetByBarcodeAsync(shopId, input.Barcode.Trim(), cancellationToken);
            if (existingItem is not null)
                itemRepository.Update(item);
        }
        else
        {
            itemRepository.Update(item);
        }

        var (batch, stockTransaction, ledgerEntry) = batchOrError.Value;
        return new InboundInventoryLineResult(item, batch, stockTransaction, ledgerEntry, inventoryOrError.Value);
    }

    private async Task<ErrorOr<Item>> ResolveItemAsync(
        Guid shopId,
        InboundInventoryLineInput input,
        Guid actorUserId,
        ItemResolutionContext itemResolutionContext,
        CancellationToken cancellationToken)
    {
        if (input.ItemId is Guid itemId)
        {
            var items = await itemRepository.GetByIdsAsync(shopId, [itemId], cancellationToken);
            var item = items.SingleOrDefault(i => i.Id == itemId && i.ShopId == shopId);
            return item is null ? Errors.PurchaseOrder.LineItemNotFound : item;
        }

        return await itemResolver.ResolveAsync(
            shopId,
            input.ItemName,
            input.Barcode,
            input.ItemDescription,
            input.Uom,
            actorUserId,
            itemResolutionContext,
            cancellationToken);
    }
}
