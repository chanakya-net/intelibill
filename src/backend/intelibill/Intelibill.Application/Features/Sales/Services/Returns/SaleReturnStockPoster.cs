using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Features.Sales.Commands.RecordSaleReturn;
using Intelibill.Application.Features.Sales.Commands.VoidSaleReturn;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Services.Returns;

/// <summary>
/// Posts a return's stock movements (restoring returned stock, or reversing that restoration on void);
/// the caller takes the batch lock first, and quarantine staging re-takes it per batch.
/// </summary>
public sealed class SaleReturnStockPoster(
    IInventoryRepository inventoryRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IStockTransactionRepository stockTransactionRepository,
    BatchDispositionService dispositions)
{
    /// <summary>
    /// Builds the return's lines and puts the restockable stock back; the caller has already locked the batches.
    /// </summary>
    public async Task<ErrorOr<StockRestoration>> RestoreStockAsync(
        RecordSaleReturnCommand command,
        SaleReturnValidationResult validated,
        string returnNumber,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken)
    {
        var calculationBySaleItemId = validated.Calculation.Lines.ToDictionary(line => line.SaleItemId);
        var returnLines = new List<SaleReturnLineInput>();
        var restorations = new List<Restoration>();

        foreach (var line in validated.Lines)
        {
            var calculated = calculationBySaleItemId[line.Request.SaleItemId];
            returnLines.Add(new SaleReturnLineInput(
                command.ShopId,
                line.SaleItem.Id,
                calculated.Quantity,
                calculated.Condition,
                calculated.OriginalCostPrice,
                calculated.OriginalSalesPrice,
                calculated.OriginalTaxRatePercent,
                calculated.OriginalIsPriceIncludingTax,
                calculated.MaxRefundAmount,
                calculated.ApprovedRefundAmount,
                calculated.TaxableAmount,
                calculated.TaxAmount,
                calculated.Notes));

            if (line.SaleItem.LineType == SaleLineType.Service || line.Request.Condition == SaleReturnCondition.Wastage)
                continue;

            var restoration = await RestoreLineStockAsync(command, line, returnNumber, processedAt, cancellationToken);
            if (restoration.IsError)
                return restoration.Errors;

            restorations.Add(restoration.Value);
        }

        return new StockRestoration(returnLines, restorations);
    }

    private async Task<ErrorOr<Restoration>> RestoreLineStockAsync(
        RecordSaleReturnCommand command,
        ValidatedSaleReturnLine line,
        string returnNumber,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken)
    {
        if (!line.SaleItem.ItemId.HasValue || !line.SaleItem.InventoryBatchId.HasValue)
            return Errors.Sale.ReturnSaleItemNotFound(line.SaleItem.Id);

        var itemId = line.SaleItem.ItemId.Value;
        var batchId = line.SaleItem.InventoryBatchId.Value;
        var batch = line.Batch;
        if (batch is null)
            return Errors.Sale.ReturnBatchNotFound(batchId);

        var inventory = await inventoryRepository.GetByItemAsync(command.ShopId, itemId, cancellationToken);
        if (inventory is null)
            return Errors.Sale.ReturnInventoryAggregateNotFound(itemId);

        var transaction = StockTransaction.Create(
            command.ShopId,
            itemId,
            batchId,
            StockTransactionType.Ret,
            line.Request.Quantity,
            returnNumber,
            line.Request.Notes,
            processedAt,
            command.ActorUserId,
            command.ActorUserId);

        if (transaction.IsError)
            return transaction.Errors;

        var inventoryResult = inventory.AddQuantity(line.Request.Quantity, command.ActorUserId);
        if (inventoryResult.IsError)
            return inventoryResult.Errors;

        var restoration = new Restoration(line, batch, transaction.Value);
        inventoryRepository.Update(inventory);
        await stockTransactionRepository.AddAsync(transaction.Value, cancellationToken);
        return restoration;
    }

    /// <summary>
    /// Stages each restored batch's disposition; a quarantine re-takes the batch lock the caller already holds.
    /// </summary>
    public async Task<ErrorOr<Success>> StageRestorationsUnderBatchLockAsync(
        RecordSaleReturnCommand command,
        SaleReturn saleReturn,
        List<Restoration> restorations,
        BusinessDay businessDay,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken)
    {
        foreach (var restoration in restorations)
        {
            var returnedLine = saleReturn.Items.Single(x => x.SaleItemId == restoration.Line.SaleItem.Id);
            var disposition = restoration.Line.Disposition!;
            var staged = await dispositions.StageRestorationAsync(command.ShopId, command.ActorUserId, restoration.Batch, "SaleReturn",
                saleReturn.Id, returnedLine.Id, restoration.Movement.Id, disposition, businessDay.Date, processedAt, cancellationToken);
            if (staged.IsError) return staged.Errors;
            returnedLine.FreezeDisposition(staged.Value, disposition.QuarantinedQuantity, disposition.ReasonCode);
            inventoryBatchRepository.Update(restoration.Batch);
        }

        return Result.Success;
    }

    /// <summary>
    /// Takes the restockable stock back out; the caller has already locked the batches, and a quarantine
    /// reversal re-takes the batch lock. The movements are returned unsaved for <see cref="AddReversalsAsync"/>.
    /// </summary>
    public async Task<ErrorOr<List<StockTransaction>>> ReverseStockUnderBatchLockAsync(
        VoidSaleReturnCommand command,
        SaleReturn saleReturn,
        Dictionary<Guid, SaleItem> saleItemsById,
        DateTimeOffset processedAt,
        DateOnly voidDate,
        CancellationToken cancellationToken)
    {
        var stockReversals = new List<StockTransaction>();

        foreach (var returnItem in saleReturn.Items.Where(item => item.Condition == SaleReturnCondition.Restockable))
        {
            if (!saleItemsById.TryGetValue(returnItem.SaleItemId, out var saleItem))
                return Errors.Sale.ReturnSaleItemNotFound(returnItem.SaleItemId);

            var reversal = await ReverseLineStockAsync(
                command,
                saleReturn,
                returnItem,
                saleItem,
                processedAt,
                voidDate,
                cancellationToken);
            if (reversal.IsError)
                return reversal.Errors;

            stockReversals.Add(reversal.Value);
        }

        return stockReversals;
    }

    private async Task<ErrorOr<StockTransaction>> ReverseLineStockAsync(
        VoidSaleReturnCommand command,
        SaleReturn saleReturn,
        SaleReturnItem returnItem,
        SaleItem saleItem,
        DateTimeOffset processedAt,
        DateOnly voidDate,
        CancellationToken cancellationToken)
    {
        if (!saleItem.ItemId.HasValue || !saleItem.InventoryBatchId.HasValue)
            return Errors.Sale.ReturnSaleItemNotFound(returnItem.SaleItemId);

        var itemId = saleItem.ItemId.Value;
        var batchId = saleItem.InventoryBatchId.Value;

        var batch = await inventoryBatchRepository.GetByIdAsync(batchId, cancellationToken);
        if (batch is null || batch.ShopId != command.ShopId)
            return Errors.Sale.ReturnBatchNotFound(batchId);

        var inventory = await inventoryRepository.GetByItemAsync(command.ShopId, itemId, cancellationToken);
        if (inventory is null)
            return Errors.Sale.ReturnInventoryAggregateNotFound(itemId);

        if (batch.Quantity < returnItem.Quantity || inventory.Quantity < returnItem.Quantity)
            return Errors.Sale.ReturnVoidInsufficientStock;

        var inventoryResult = inventory.SubtractQuantity(returnItem.Quantity, command.ActorUserId);
        if (inventoryResult.IsError)
            return inventoryResult.Errors;

        var stockTransaction = StockTransaction.Create(
            command.ShopId,
            itemId,
            batchId,
            StockTransactionType.Out,
            -returnItem.Quantity,
            saleReturn.ReturnNumber,
            command.Reason,
            processedAt,
            command.ActorUserId,
            command.ActorUserId);
        if (stockTransaction.IsError)
            return stockTransaction.Errors;

        ErrorOr<Success> batchResult;
        if (returnItem.QuarantineChangeId.HasValue)
            batchResult = await dispositions.StageReversalAsync(command.ShopId, command.ActorUserId, batch, returnItem.QuarantineChangeId.Value,
                returnItem.QuarantinedQuantity, stockTransaction.Value.Id, voidDate, saleReturn.Id, returnItem.Id, processedAt, cancellationToken);
        else
            batchResult = batch.SubtractQuantity(returnItem.Quantity, command.ActorUserId);
        if (batchResult.IsError) return batchResult.Errors;
        inventoryBatchRepository.Update(batch);
        inventoryRepository.Update(inventory);
        return stockTransaction.Value;
    }

    /// <summary>Adds the void's stock reversal movements, in order, to the unit of work.</summary>
    public async Task AddReversalsAsync(List<StockTransaction> stockReversals, CancellationToken cancellationToken)
    {
        foreach (var stockReversal in stockReversals)
            await stockTransactionRepository.AddAsync(stockReversal, cancellationToken);
    }

    public sealed record Restoration(ValidatedSaleReturnLine Line, InventoryBatch Batch, StockTransaction Movement);

    public sealed record StockRestoration(List<SaleReturnLineInput> ReturnLines, List<Restoration> Restorations);
}
