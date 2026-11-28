using ErrorOr;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Commands.RecordSale;

/// <summary>Posts a sale's stock deduction and the customer due it leaves on the customer ledger.</summary>
public sealed class SaleStockAndDuePoster(
    SaleStockOutPoster stockOutPoster,
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
            var stockOut = await stockOutPoster.PostStockOutAsync(
                command.ShopId,
                item.Id,
                batch,
                inventory,
                cmdItem.Quantity,
                invoiceNumber,
                soldAt,
                command.ActorUserId,
                command.ActorUserId,
                cancellationToken);
            if (stockOut.IsError) return stockOut.Errors;
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
