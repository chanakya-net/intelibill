using Intelibill.Application.Features.Exports.Sales.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Exports.Sales.Services;

public sealed class SalesExportDatasetBuilder : ISalesExportDatasetBuilder
{
    private readonly ISaleRepository _saleRepository;
    private readonly ISaleReturnRepository _saleReturnRepository;
    private readonly IItemRepository _itemRepository;

    public SalesExportDatasetBuilder(
        ISaleRepository saleRepository,
        ISaleReturnRepository saleReturnRepository,
        IItemRepository itemRepository)
    {
        _saleRepository = saleRepository;
        _saleReturnRepository = saleReturnRepository;
        _itemRepository = itemRepository;
    }

    public async Task<SalesExportDatasetDto> BuildAsync(
        Shop shop,
        User generatedBy,
        DateOnly startDate,
        DateOnly endDate,
        string level,
        CancellationToken cancellationToken)
    {
        var sales = await _saleRepository.GetByShopAndDateRangeAsync(shop.Id, startDate, endDate, cancellationToken);
        
        var itemIds = sales.SelectMany(s => s.Items).Select(i => i.ItemId).Distinct().ToList();
        var items = await _itemRepository.GetByIdsAsync(shop.Id, itemIds, cancellationToken);
        var itemNameById = items.ToDictionary(i => i.Id, i => i.Name);

        var summaryRows = new List<SalesExportSummaryRowDto>();
        var lineItemRows = new List<SalesExportLineItemRowDto>();
        var taxBreakupMap = new Dictionary<decimal, TaxBreakupAccumulator>();

        foreach (var sale in sales)
        {
            var returns = (await _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, cancellationToken))
                .Where(r => !r.IsVoided)
                .ToList();

            var returnNumbers = string.Join(", ", returns.Select(r => r.ReturnNumber));
            var totalReturnAmount = returns.Sum(r => r.TotalRefundAmount);
            var totalReturnTaxable = returns.Sum(r => r.TotalTaxableAmount);
            var totalReturnTax = returns.Sum(r => r.TotalTaxAmount);

            summaryRows.Add(new SalesExportSummaryRowDto(
                sale.InvoiceNumber,
                sale.SoldAt,
                sale.CustomerName,
                sale.PaymentMethod.ToString(),
                sale.PaidAmount,
                sale.DueAmount,
                sale.TotalBeforeDiscount,
                sale.TotalDiscountAmount,
                sale.Items.Sum(i => i.TaxableAmount),
                sale.TotalTaxAmount,
                sale.TotalAmount,
                string.IsNullOrWhiteSpace(returnNumbers) ? null : returnNumbers,
                totalReturnAmount,
                totalReturnTaxable,
                totalReturnTax,
                sale.TotalAmount - totalReturnAmount,
                returns.Count > 0,
                sale.Items.Count));

            foreach (var saleItem in sale.Items)
            {
                if (!taxBreakupMap.TryGetValue(saleItem.TaxRatePercent, out var accumulator))
                {
                    accumulator = new TaxBreakupAccumulator { TaxRatePercent = saleItem.TaxRatePercent };
                    taxBreakupMap[saleItem.TaxRatePercent] = accumulator;
                }
                accumulator.SaleTaxableAmount += saleItem.TaxableAmount;
                accumulator.SaleTaxAmount += saleItem.TaxAmount;

                if (level.Equals(SalesExportLevel.LineItems, StringComparison.OrdinalIgnoreCase))
                {
                    var itemReturns = returns.SelectMany(r => r.Items).Where(ri => ri.SaleItemId == saleItem.Id).ToList();
                    var returnedQuantity = itemReturns.Sum(ri => ri.Quantity);
                    var itemReturnNumbers = string.Join(", ", returns.Where(r => r.Items.Any(ri => ri.SaleItemId == saleItem.Id)).Select(r => r.ReturnNumber));

                    lineItemRows.Add(new SalesExportLineItemRowDto(
                        sale.InvoiceNumber,
                        sale.CustomerName,
                        itemNameById.GetValueOrDefault(saleItem.ItemId, "Unknown Item"),
                        saleItem.Quantity,
                        saleItem.SalesPrice,
                        saleItem.ItemDiscountAmount + saleItem.SaleDiscountAmount,
                        saleItem.TaxRatePercent,
                        saleItem.TaxableAmount,
                        saleItem.TaxAmount,
                        saleItem.TotalAmount,
                        saleItem.IsPriceIncludingTax,
                        returnedQuantity,
                        GetReturnStatus(returnedQuantity, saleItem.Quantity),
                        string.IsNullOrWhiteSpace(itemReturnNumbers) ? null : itemReturnNumbers));
                }
            }

            foreach (var r in returns)
            {
                foreach (var ri in r.Items)
                {
                    if (!taxBreakupMap.TryGetValue(ri.OriginalTaxRatePercent, out var accumulator))
                    {
                        accumulator = new TaxBreakupAccumulator { TaxRatePercent = ri.OriginalTaxRatePercent };
                        taxBreakupMap[ri.OriginalTaxRatePercent] = accumulator;
                    }
                    accumulator.ReturnTaxableAmount += ri.TaxableAmount;
                    accumulator.ReturnTaxAmount += ri.TaxAmount;
                }
            }
        }

        var taxBreakup = taxBreakupMap.Values
            .Select(a => new SalesExportTaxBreakupDto(
                a.TaxRatePercent,
                a.SaleTaxableAmount,
                a.SaleTaxAmount,
                a.ReturnTaxableAmount,
                a.ReturnTaxAmount))
            .OrderBy(t => t.TaxRatePercent)
            .ToList();

        var metadata = new SalesExportMetadataDto(
            shop.Name,
            $"{shop.Address}, {shop.City}, {shop.State} - {shop.Pincode}",
            shop.GstNumber,
            $"{generatedBy.FirstName} {generatedBy.LastName}".Trim(),
            DateTimeOffset.UtcNow,
            startDate,
            endDate,
            level);

        return new SalesExportDatasetDto(
            metadata,
            summaryRows,
            lineItemRows,
            taxBreakup);
    }

    private static string GetReturnStatus(decimal returnedQuantity, decimal soldQuantity)
    {
        if (returnedQuantity <= 0) return "NotReturned";
        return returnedQuantity >= soldQuantity ? "FullyReturned" : "PartiallyReturned";
    }

    private class TaxBreakupAccumulator
    {
        public decimal TaxRatePercent { get; set; }
        public decimal SaleTaxableAmount { get; set; }
        public decimal SaleTaxAmount { get; set; }
        public decimal ReturnTaxableAmount { get; set; }
        public decimal ReturnTaxAmount { get; set; }
    }
}
