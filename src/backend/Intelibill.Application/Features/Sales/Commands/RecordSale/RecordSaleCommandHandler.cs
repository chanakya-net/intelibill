using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Commands.RecordSale;

public sealed class RecordSaleCommandHandler(
    IItemRepository itemRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IInventoryRepository inventoryRepository,
    IStockTransactionRepository stockTransactionRepository,
    ISaleRepository saleRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<SaleDto>> HandleAsync(RecordSaleCommand command, CancellationToken cancellationToken)
    {
        // 1. Bulk fetch items by barcode
        var barcodes = command.Items.Select(i => i.Barcode).Distinct().ToList();
        var items = await itemRepository.GetByBarcodesAsync(command.ShopId, barcodes, cancellationToken);
        var itemsByBarcode = items.ToDictionary(i => i.Barcode, StringComparer.OrdinalIgnoreCase);

        // 2. Resolve item per line — fail fast if not found
        var lineContexts = new List<(RecordSaleItemCommand Cmd, Item Item)>();
        foreach (var cmdItem in command.Items)
        {
            if (!itemsByBarcode.TryGetValue(cmdItem.Barcode, out var item))
                return Errors.Sale.ItemNotFound(cmdItem.Barcode);
            lineContexts.Add((cmdItem, item));
        }

        // 3. Bulk fetch batches
        var itemIds = lineContexts.Select(c => c.Item.Id).Distinct().ToList();
        var batchNumbers = lineContexts.Select(c => c.Cmd.BatchNumber.Trim()).Distinct().ToList();
        var batches = await inventoryBatchRepository.GetByItemIdsAndBatchNumbersAsync(
            command.ShopId, itemIds, batchNumbers, cancellationToken);
        var batchMap = batches.ToDictionary(b => (b.ItemId, b.BatchNumber));

        // 4. Bulk fetch inventory aggregates
        var inventories = await inventoryRepository.GetByItemIdsAsync(command.ShopId, itemIds, cancellationToken);
        var inventoryByItemId = inventories.ToDictionary(i => i.ItemId);

        // 5. Validate each line
        var warnings = new List<string>();
        var validatedLines = new List<(RecordSaleItemCommand Cmd, Item Item, InventoryBatch Batch, Domain.Entities.Inventory Inventory, bool HasMismatch)>();

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

            validatedLines.Add((cmdItem, item, batch, inventory, hasMismatch));
        }

        // 6. Generate invoice number
        var guidPart = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var invoiceNumber = $"INV-{DateTimeOffset.UtcNow:yyyyMMdd}-{guidPart}";

        // 7. Mutate + build sale items
        var saleItems = new List<SaleItem>();
        decimal totalAmount = 0m;
        decimal totalTaxAmount = 0m;

        foreach (var (cmdItem, item, batch, inventory, hasMismatch) in validatedLines)
        {
            var batchResult = batch.SubtractQuantity(cmdItem.Quantity, command.ActorUserId);
            if (batchResult.IsError) return batchResult.Errors;
            inventory.SubtractQuantity(cmdItem.Quantity, command.ActorUserId);

            var txResult = StockTransaction.Create(
                command.ShopId,
                item.Id,
                batch.Id,
                StockTransactionType.Out,
                -cmdItem.Quantity,
                invoiceNumber,
                null,
                DateTimeOffset.UtcNow,
                command.ActorUserId,
                command.ActorUserId);

            if (txResult.IsError)
                return txResult.Errors;

            await stockTransactionRepository.AddAsync(txResult.Value, cancellationToken);

            decimal taxAmount;
            if (cmdItem.IsPriceIncludingTax && cmdItem.TaxRatePercent > 0)
                taxAmount = cmdItem.Quantity * cmdItem.SalesPrice * cmdItem.TaxRatePercent / (100 + cmdItem.TaxRatePercent);
            else
                taxAmount = cmdItem.Quantity * cmdItem.SalesPrice * cmdItem.TaxRatePercent / 100;

            totalAmount += cmdItem.Quantity * cmdItem.SalesPrice;
            totalTaxAmount += taxAmount;

            saleItems.Add(SaleItem.Create(
                command.ShopId,
                item.Id,
                batch.Id,
                cmdItem.Quantity,
                cmdItem.CostPrice,
                cmdItem.SalesPrice,
                cmdItem.Mrp,
                cmdItem.TaxRatePercent,
                cmdItem.IsPriceIncludingTax,
                hasMismatch));
        }

        // 8. Build sale aggregate
        var sale = Sale.Create(
            command.ShopId,
            invoiceNumber,
            command.CustomerId,
            command.CustomerName,
            command.CustomerPhone,
            command.PaymentMethod,
            DateTimeOffset.UtcNow,
            totalAmount,
            totalTaxAmount,
            saleItems);

        await saleRepository.AddAsync(sale, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var itemNameById = items.ToDictionary(i => i.Id, i => i.Name);

        return new SaleDto(
            sale.Id,
            sale.InvoiceNumber,
            sale.PaymentMethod,
            sale.SoldAt,
            sale.TotalAmount,
            sale.TotalTaxAmount,
            sale.Items.Select(si => new SaleItemDto(
                si.Id,
                si.ItemId,
                itemNameById.GetValueOrDefault(si.ItemId, "Unknown Item"),
                si.InventoryBatchId,
                si.Quantity,
                si.SalesPrice,
                si.TaxRatePercent,
                si.IsPriceIncludingTax,
                si.HasPriceMismatch)).ToList(),
            warnings);
    }
}
