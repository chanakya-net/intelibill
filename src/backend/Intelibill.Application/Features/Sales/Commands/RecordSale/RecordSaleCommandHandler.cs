using ErrorOr;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Commands.RecordSale;

public sealed class RecordSaleCommandHandler(
    ISaleLineValidator saleLineValidator,
    ICustomerResolver customerResolver,
    ISaleRepository saleRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository,
    IStockTransactionRepository stockTransactionRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<SaleDto>> HandleAsync(RecordSaleCommand command, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        var validationResultOrError = await saleLineValidator.ValidateLinesAsync(
            command.ShopId, command.Items, warnings, cancellationToken);

        if (validationResultOrError.IsError)
            return validationResultOrError.Errors;

        var (validatedLines, itemNameById) = validationResultOrError.Value;

        var invoiceNumber = $"INV-{DateTimeOffset.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

        var saleLineInputs = new List<SaleLineInput>(validatedLines.Count);
        foreach (var (cmdItem, item, batch, inventory, hasMismatch) in validatedLines)
        {
            var batchResult = batch.SubtractQuantity(cmdItem.Quantity, command.ActorUserId);
            if (batchResult.IsError) return batchResult.Errors;

            var inventoryResult = inventory.SubtractQuantity(cmdItem.Quantity, command.ActorUserId);
            if (inventoryResult.IsError) return inventoryResult.Errors;

            var txResult = StockTransaction.Create(
                command.ShopId, item.Id, batch.Id,
                StockTransactionType.Out, -cmdItem.Quantity,
                invoiceNumber, null, DateTimeOffset.UtcNow,
                command.ActorUserId, command.ActorUserId);

            if (txResult.IsError) return txResult.Errors;

            await stockTransactionRepository.AddAsync(txResult.Value, cancellationToken);

            saleLineInputs.Add(new SaleLineInput(
                command.ShopId,
                item.Id,
                batch.Id,
                cmdItem.Quantity,
                batch.CostPrice,
                cmdItem.SalesPrice,
                cmdItem.Mrp,
                cmdItem.TaxRatePercent,
                cmdItem.IsPriceIncludingTax,
                hasMismatch));
        }

        var normalizedCustomerPhone = string.IsNullOrWhiteSpace(command.CustomerPhone) ? null : command.CustomerPhone.Trim();

        var resolvedCustomerOrError = await customerResolver.ResolveAsync(
            command.ShopId,
            command.CustomerId,
            command.CustomerPhone,
            command.DueAmount > 0,
            command.PaymentMethod,
            cancellationToken);

        if (resolvedCustomerOrError.IsError)
            return resolvedCustomerOrError.Errors;

        var resolvedCustomer = resolvedCustomerOrError.Value;

        var saleOrError = Sale.Record(
            command.ShopId,
            invoiceNumber,
            saleLineInputs,
            resolvedCustomer?.Id ?? command.CustomerId,
            resolvedCustomer?.Name ?? command.CustomerName,
            resolvedCustomer?.PhoneNumber ?? normalizedCustomerPhone,
            command.PaymentMethod,
            command.PaidAmount,
            command.DueAmount,
            DateTimeOffset.UtcNow);

        if (saleOrError.IsError)
            return saleOrError.Errors;

        var sale = saleOrError.Value;
        await saleRepository.AddAsync(sale, cancellationToken);

        if (sale.DueAmount > 0 && sale.CustomerId.HasValue)
        {
            var ledgerResult = CustomerLedgerEntry.Create(
                sale.ShopId,
                sale.CustomerId.Value,
                sale.Id,
                CustomerLedgerEntryType.SaleDue,
                sale.DueAmount,
                DateOnly.FromDateTime(sale.SoldAt.UtcDateTime),
                $"Due recorded from sale {sale.InvoiceNumber}",
                command.ActorUserId);

            if (ledgerResult.IsError)
                return ledgerResult.Errors;

            await customerLedgerEntryRepository.AddAsync(ledgerResult.Value, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new SaleDto(
            sale.Id,
            sale.InvoiceNumber,
            sale.CustomerId,
            sale.PaymentMethod,
            sale.SoldAt,
            sale.PaidAmount,
            sale.DueAmount,
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
