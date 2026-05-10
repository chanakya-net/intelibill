using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Domain.Entities;
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
        var lineContexts = new List<(RecordSaleItemCommand Cmd, InventoryBatch Batch)>();
        foreach (var cmdItem in items)
        {
            var batch = await inventoryBatchRepository.GetByIdWithItemAsync(
                cmdItem.InventoryBatchId,
                shopId,
                cancellationToken);

            if (batch is null)
                return Errors.Sale.BatchNotFound(cmdItem.Barcode, cmdItem.BatchNumber);
            lineContexts.Add((cmdItem, batch));
        }

        var itemIds = lineContexts.Select(c => c.Batch.ItemId).Distinct().ToList();
        var dbItems = await itemRepository.GetByIdsAsync(shopId, itemIds, cancellationToken);
        var itemNameById = dbItems.ToDictionary(i => i.Id, i => i.Name);
        var itemsById = dbItems.ToDictionary(i => i.Id);

        var resolvedContexts = new List<(RecordSaleItemCommand Cmd, Item Item, InventoryBatch Batch)>(lineContexts.Count);
        foreach (var (cmdItem, batch) in lineContexts)
        {
            if (!itemsById.TryGetValue(batch.ItemId, out var item))
                return Errors.Sale.ItemNotFound(cmdItem.Barcode);

            resolvedContexts.Add((cmdItem, item, batch));
        }

        var inventories = await inventoryRepository.GetByItemIdsAsync(shopId, itemIds, cancellationToken);
        var inventoryByItemId = inventories.ToDictionary(i => i.ItemId);
        var requestedQuantityByBatchId = items
            .GroupBy(i => i.InventoryBatchId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        var validated = new List<ValidatedSaleLine>();

        foreach (var (cmdItem, item, batch) in resolvedContexts)
        {
            if (!item.IsActive)
                return Errors.Sale.ItemInactive(cmdItem.Barcode);

            if (batch.IsVoided)
                return Errors.Sale.BatchVoided(cmdItem.Barcode, cmdItem.BatchNumber);

            if (requestedQuantityByBatchId[batch.Id] > batch.Quantity)
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
