using ErrorOr;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Commands.RecordSale;

/// <summary>Posts a sale's stock deduction and the customer due it leaves on the customer ledger.</summary>
public sealed class SaleStockAndDuePoster(
    IStockTransactionRepository stockTransactionRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository)
{
    internal async Task<ErrorOr<Success>> DeductStockAsync(
        RecordSaleCommand command,
        IReadOnlyList<ValidatedSaleLine> validatedLines,
        string invoiceNumber,
        DateTimeOffset soldAt,
        CancellationToken cancellationToken)
    {
        foreach (var line in validatedLines.Where(x => x.LineType == SaleLineType.Goods))
        {
            var cmdItem = line.Command;
            var item = line.Item!;
            var batch = line.Batch!;
            var inventory = line.Inventory!;
            if (cmdItem.Quantity > batch.UnquarantinedQuantity)
                return Error.Validation("InventoryBatch.InsufficientStock", "Insufficient unquarantined stock in batch.");
            var batchResult = batch.SubtractQuantity(cmdItem.Quantity, command.ActorUserId);
            if (batchResult.IsError) return batchResult.Errors;

            var inventoryResult = inventory.SubtractQuantity(cmdItem.Quantity, command.ActorUserId);
            if (inventoryResult.IsError) return inventoryResult.Errors;

            var txResult = StockTransaction.Create(
                command.ShopId,
                item.Id,
                batch.Id,
                StockTransactionType.Out,
                -cmdItem.Quantity,
                invoiceNumber,
                null,
                soldAt,
                command.ActorUserId,
                command.ActorUserId);
            if (txResult.IsError) return txResult.Errors;
            await stockTransactionRepository.AddAsync(txResult.Value, cancellationToken);
        }

        return Result.Success;
    }

    internal async Task<ErrorOr<Success>> PostCustomerDueAsync(
        RecordSaleCommand command,
        Sale sale,
        CancellationToken cancellationToken)
    {
        if (sale.DueAmount > 0 && sale.CustomerId.HasValue)
        {
            var ledgerResult = CustomerLedgerEntry.Create(
                sale.ShopId,
                sale.CustomerId.Value,
                sale.Id,
                CustomerLedgerEntryType.SaleDue,
                sale.DueAmount,
                sale.BusinessDate,
                $"Due recorded from sale {sale.InvoiceNumber}",
                command.ActorUserId);
            if (ledgerResult.IsError)
                return ledgerResult.Errors;

            await customerLedgerEntryRepository.AddAsync(ledgerResult.Value, cancellationToken);
        }

        return Result.Success;
    }
}
