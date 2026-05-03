using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Services;

internal sealed class SaleLineValidator(
    IItemRepository itemRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IInventoryRepository inventoryRepository)
    : ISaleLineValidator
{
    public async Task<ErrorOr<SaleLineValidationResult>> ValidateLinesAsync(
        Guid shopId,
        IReadOnlyList<RecordSaleItemCommand> items,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var barcodes = items.Select(i => i.Barcode).Distinct().ToList();
        var dbItems = await itemRepository.GetByBarcodesAsync(shopId, barcodes, cancellationToken);
        var itemsByBarcode = dbItems.ToDictionary(i => i.Barcode, StringComparer.OrdinalIgnoreCase);
        var itemNameById = dbItems.ToDictionary(i => i.Id, i => i.Name);

        var lineContexts = new List<(RecordSaleItemCommand Cmd, Item Item)>();
        foreach (var cmdItem in items)
        {
            if (!itemsByBarcode.TryGetValue(cmdItem.Barcode, out var item))
                return Errors.Sale.ItemNotFound(cmdItem.Barcode);
            lineContexts.Add((cmdItem, item));
        }

        var itemIds = lineContexts.Select(c => c.Item.Id).Distinct().ToList();
        var batchNumbers = lineContexts.Select(c => c.Cmd.BatchNumber.Trim()).Distinct().ToList();
        var batches = await inventoryBatchRepository.GetByItemIdsAndBatchNumbersAsync(
            shopId, itemIds, batchNumbers, cancellationToken);
        var batchMap = batches.ToDictionary(b => (b.ItemId, b.BatchNumber));

        var inventories = await inventoryRepository.GetByItemIdsAsync(shopId, itemIds, cancellationToken);
        var inventoryByItemId = inventories.ToDictionary(i => i.ItemId);

        var validated = new List<ValidatedSaleLine>();

        foreach (var (cmdItem, item) in lineContexts)
        {
            var batchKey = (item.Id, cmdItem.BatchNumber.Trim());
            if (!batchMap.TryGetValue(batchKey, out var batch))
                return Errors.Sale.BatchNotFound(cmdItem.Barcode, cmdItem.BatchNumber);

            if (batch.IsVoided)
                return Errors.Sale.BatchVoided(cmdItem.Barcode, cmdItem.BatchNumber);

            if (cmdItem.Quantity > batch.Quantity)
                return Errors.Sale.InsufficientStock(cmdItem.Barcode, cmdItem.BatchNumber);

            if (!inventoryByItemId.TryGetValue(item.Id, out var inventory))
                return Errors.Sale.InventoryAggregateNotFound(cmdItem.Barcode);

            var hasMismatch = cmdItem.CostPrice != batch.CostPrice
                || cmdItem.SalesPrice != batch.SalesPrice
                || cmdItem.Mrp != batch.Mrp
                || cmdItem.TaxRatePercent != batch.TaxRatePercent;

            if (hasMismatch)
                warnings.Add($"Price mismatch for item '{item.Name}' (barcode: {item.Barcode}, batch: {batch.BatchNumber}).");

            if (!string.Equals(cmdItem.ItemName.Trim(), item.Name, StringComparison.OrdinalIgnoreCase))
                warnings.Add($"Item name mismatch for barcode '{cmdItem.Barcode}': provided '{cmdItem.ItemName}', found '{item.Name}'.");

            validated.Add(new ValidatedSaleLine(cmdItem, item, batch, inventory, hasMismatch));
        }

        return new SaleLineValidationResult(validated, itemNameById);
    }
}
