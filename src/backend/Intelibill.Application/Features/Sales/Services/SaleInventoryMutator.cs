using ErrorOr;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Services;

internal sealed class SaleInventoryMutator(
    IStockTransactionRepository stockTransactionRepository)
    : ISaleInventoryMutator
{
    public async Task<ErrorOr<MutatedSaleLine>> MutateAsync(
        Guid shopId,
        string invoiceNumber,
        ValidatedSaleLine validatedLine,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var (cmdItem, item, batch, inventory, hasMismatch) = validatedLine;

        var batchResult = batch.SubtractQuantity(cmdItem.Quantity, actorUserId);
        if (batchResult.IsError) return batchResult.Errors;

        var inventoryResult = inventory.SubtractQuantity(cmdItem.Quantity, actorUserId);
        if (inventoryResult.IsError) return inventoryResult.Errors;

        var txResult = StockTransaction.Create(
            shopId,
            item.Id,
            batch.Id,
            StockTransactionType.Out,
            -cmdItem.Quantity,
            invoiceNumber,
            null,
            DateTimeOffset.UtcNow,
            actorUserId,
            actorUserId);

        if (txResult.IsError)
            return txResult.Errors;

        var stockTx = txResult.Value;
        await stockTransactionRepository.AddAsync(stockTx, cancellationToken);

        decimal taxAmount;
        if (cmdItem.IsPriceIncludingTax && cmdItem.TaxRatePercent > 0)
            taxAmount = cmdItem.Quantity * cmdItem.SalesPrice * cmdItem.TaxRatePercent / (100 + cmdItem.TaxRatePercent);
        else
            taxAmount = cmdItem.Quantity * cmdItem.SalesPrice * cmdItem.TaxRatePercent / 100;

        var saleItem = SaleItem.Create(
            shopId,
            item.Id,
            batch.Id,
            cmdItem.Quantity,
            batch.CostPrice,
            cmdItem.SalesPrice,
            cmdItem.Mrp,
            cmdItem.TaxRatePercent,
            cmdItem.IsPriceIncludingTax,
            hasMismatch);

        return new MutatedSaleLine(saleItem, stockTx, taxAmount);
    }
}
