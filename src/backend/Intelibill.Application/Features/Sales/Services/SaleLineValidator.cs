using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Services;

internal sealed class SaleLineValidator(
    IItemRepository itemRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IInventoryRepository inventoryRepository,
    IServiceRepository? serviceRepository = null)
    : ISaleLineValidator
{
    public async Task<ErrorOr<SaleLineValidationResult>> ValidateLinesAsync(
        Guid shopId,
        IReadOnlyList<RecordSaleItemCommand> items,
        List<string> warnings,
        CancellationToken cancellationToken,
        bool allowInsufficientStock = false)
    {
        var itemNameById = new Dictionary<Guid, string>();
        var validatedByIndex = new Dictionary<int, ValidatedSaleLine>(items.Count);
        var indexedItems = items.Select((command, index) => (Index: index, Command: command)).ToList();

        foreach (var (_, command) in indexedItems)
        {
            if (!command.IsGoodsLine && !command.IsServiceLine)
            {
                return Errors.Sale.InvalidLineType;
            }
        }

        var goodsLines = indexedItems.Where(x => x.Command.IsGoodsLine).ToList();
        var serviceLines = indexedItems.Where(x => x.Command.IsServiceLine).ToList();

        if (goodsLines.Count > 0)
        {
            var goodsOrError = await ValidateGoodsLinesAsync(
                shopId,
                goodsLines.Select(x => x.Command).ToList(),
                warnings,
                allowInsufficientStock,
                cancellationToken);
            if (goodsOrError.IsError)
            {
                return goodsOrError.Errors;
            }

            foreach (var pair in goodsOrError.Value.ItemNameById)
            {
                itemNameById[pair.Key] = pair.Value;
            }

            for (var i = 0; i < goodsLines.Count; i++)
            {
                validatedByIndex[goodsLines[i].Index] = goodsOrError.Value.Lines[i];
            }
        }

        foreach (var (index, cmd) in serviceLines)
        {
            if (serviceRepository is null)
            {
                return Errors.Sale.ServiceNotFound;
            }
            if (!IsValidTaxRatePercent(cmd.TaxRatePercent))
            {
                return Errors.Sale.InvalidTaxRatePercent;
            }

            if (!string.IsNullOrWhiteSpace(cmd.HsnCode) && !IsValidHsnCode(cmd.HsnCode))
            {
                return Errors.Sale.InvalidHsnCode;
            }

            if (cmd.ServiceId is null)
            {
                return Errors.Sale.ServiceNotFound;
            }

            if (cmd.ItemDiscount is not null && cmd.ItemDiscount.Type != InstantDiscountType.None)
            {
                return Errors.Sale.ServiceDiscountNotSupported;
            }

            var service = await serviceRepository.GetByIdAsync(cmd.ServiceId.Value, cancellationToken);
            if (service is null || service.ShopId != shopId)
            {
                return Errors.Sale.ServiceNotFound;
            }

            if (!service.IsActive)
            {
                return Errors.Sale.ServiceInactive;
            }

            var hasMismatch = cmd.SalesPrice != service.Price
                || cmd.TaxRatePercent != service.TaxRatePercent
                || cmd.IsPriceIncludingTax != service.TaxIncluded;

            if (hasMismatch)
            {
                warnings.Add($"Price mismatch for service '{service.Name}' (code: {service.Code}).");
            }

            if (!string.Equals(cmd.ItemName.Trim(), service.Name, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Service name mismatch for code '{service.Code}': provided '{cmd.ItemName}', found '{service.Name}'.");
            }

            validatedByIndex[index] = new ValidatedSaleLine(cmd, SaleLineType.Service, null, null, null, service, hasMismatch);
        }

        var validated = new List<ValidatedSaleLine>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            validated.Add(validatedByIndex[i]);
        }

        return new SaleLineValidationResult(validated, itemNameById);
    }

    private async Task<ErrorOr<SaleLineValidationResult>> ValidateGoodsLinesAsync(
        Guid shopId,
        IReadOnlyList<RecordSaleItemCommand> items,
        List<string> warnings,
        bool allowInsufficientStock,
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
            if (!IsValidTaxRatePercent(cmdItem.TaxRatePercent))
                return Errors.Sale.InvalidTaxRatePercent;

            if (!string.IsNullOrWhiteSpace(cmdItem.HsnCode) && !IsValidHsnCode(cmdItem.HsnCode))
                return Errors.Sale.InvalidHsnCode;

            if (!item.IsActive)
                return Errors.Sale.ItemInactive(cmdItem.Barcode);

            if (batch.IsVoided)
                return Errors.Sale.BatchVoided(cmdItem.Barcode, cmdItem.BatchNumber);

            if (!allowInsufficientStock && requestedQuantityByBatchId[batch.Id] > batch.Quantity)
                return Errors.Sale.InsufficientStock(cmdItem.Barcode, cmdItem.BatchNumber);

            if (!inventoryByItemId.TryGetValue(item.Id, out var inventory))
                return Errors.Sale.InventoryAggregateNotFound(cmdItem.Barcode);

            var hasMismatch = cmdItem.CostPrice != batch.CostPrice
                || cmdItem.SalesPrice != batch.SalesPrice
                || cmdItem.Mrp != batch.Mrp
                || cmdItem.TaxRatePercent != batch.TaxRatePercent
                || cmdItem.IsPriceIncludingTax != batch.TaxIncluded;

            if (hasMismatch)
                warnings.Add($"Price mismatch for item '{item.Name}' (barcode: {item.Barcode}, batch: {batch.BatchNumber}).");

            if (!string.Equals(cmdItem.ItemName.Trim(), item.Name, StringComparison.OrdinalIgnoreCase))
                warnings.Add($"Item name mismatch for barcode '{cmdItem.Barcode}': provided '{cmdItem.ItemName}', found '{item.Name}'.");

            validated.Add(new ValidatedSaleLine(cmdItem, SaleLineType.Goods, item, batch, inventory, null, hasMismatch));
        }

        return new SaleLineValidationResult(validated, itemNameById);
    }

    private static bool IsValidHsnCode(string value)
    {
        var normalized = value.Trim();
        return normalized.Length is >= 4 and <= 8 && normalized.All(char.IsDigit);
    }

    private static bool IsValidTaxRatePercent(decimal value)
    {
        if (value < 0m || value > 100m)
        {
            return false;
        }

        return GetScale(value) <= 2;
    }

    private static int GetScale(decimal value)
    {
        var bits = decimal.GetBits(value);
        return (bits[3] >> 16) & 0x7F;
    }
}
