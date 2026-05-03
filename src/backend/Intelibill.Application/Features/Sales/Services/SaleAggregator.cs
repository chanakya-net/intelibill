using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Services;

internal sealed class SaleAggregator(
    ISaleRepository saleRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository) : ISaleAggregator
{
    public async Task<ErrorOr<SaleAggregation>> AggregateAsync(
        string invoiceNumber,
        Guid shopId,
        decimal paidAmount,
        decimal dueAmount,
        Guid actorUserId,
        Customer? resolvedCustomer,
        string? customerName,
        string? customerPhone,
        PaymentMethod paymentMethod,
        IReadOnlyList<MutatedSaleLine> mutatedLines,
        List<string> warnings,
        IReadOnlyDictionary<Guid, string> itemNameById,
        CancellationToken cancellationToken)
    {
        var saleItems = mutatedLines.Select(m => m.SaleItem).ToList();

        var totalAmount = mutatedLines.Sum(m =>
        {
            var lineTotal = m.SaleItem.SalesPrice * m.SaleItem.Quantity;
            if (!m.SaleItem.IsPriceIncludingTax)
                lineTotal += m.CalculatedTax;
            return lineTotal;
        });

        var totalTaxAmount = mutatedLines.Sum(m => m.CalculatedTax);

        var roundedCalculatedTotal = decimal.Round(totalAmount, 2, MidpointRounding.AwayFromZero);
        var roundedSplitTotal = decimal.Round(paidAmount + dueAmount, 2, MidpointRounding.AwayFromZero);

        if (roundedCalculatedTotal != roundedSplitTotal)
            return Errors.Sale.PaidAndDueAmountMismatch;

        if (paymentMethod == PaymentMethod.Credit && dueAmount <= 0)
            return Errors.Sale.CreditRequiresDueAmount;

        var sale = Sale.Create(
            shopId,
            invoiceNumber,
            resolvedCustomer?.Id,
            resolvedCustomer?.Name ?? customerName,
            resolvedCustomer?.PhoneNumber ?? customerPhone,
            paymentMethod,
            DateTimeOffset.UtcNow,
            paidAmount,
            dueAmount,
            totalAmount,
            totalTaxAmount,
            saleItems);

        await saleRepository.AddAsync(sale, cancellationToken);

        CustomerLedgerEntry? ledgerEntry = null;
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
                actorUserId);

            if (ledgerResult.IsError)
                return ledgerResult.Errors;

            ledgerEntry = ledgerResult.Value;
            await customerLedgerEntryRepository.AddAsync(ledgerEntry, cancellationToken);
        }

        var dto = new SaleDto(
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

        return new SaleAggregation(sale, ledgerEntry, dto);
    }
}
