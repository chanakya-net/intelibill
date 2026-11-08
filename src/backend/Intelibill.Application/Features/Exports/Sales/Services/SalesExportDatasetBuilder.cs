using Intelibill.Application.Features.Exports.Accounting;
using Intelibill.Application.Features.Exports.Sales.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Exports.Sales.Services;

public sealed class SalesExportDatasetBuilder : ISalesExportDatasetBuilder
{
    private readonly ISaleRepository _saleRepository;
    private readonly ISaleReturnRepository _saleReturnRepository;
    private readonly IItemRepository _itemRepository;
    private readonly ReturnAdjustmentReader _returnAdjustmentReader;
    private readonly SaleCancellationAdjustmentReader _cancellationAdjustmentReader;

    public SalesExportDatasetBuilder(
        ISaleRepository saleRepository,
        ISaleReturnRepository saleReturnRepository,
        IItemRepository itemRepository,
        ReturnAdjustmentReader returnAdjustmentReader,
        SaleCancellationAdjustmentReader cancellationAdjustmentReader)
    {
        _saleRepository = saleRepository;
        _saleReturnRepository = saleReturnRepository;
        _itemRepository = itemRepository;
        _returnAdjustmentReader = returnAdjustmentReader;
        _cancellationAdjustmentReader = cancellationAdjustmentReader;
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
        var cancellations = await _cancellationAdjustmentReader.ReadAsync(shop.Id, startDate, endDate, cancellationToken);
        var cancellationBySale = (await _cancellationAdjustmentReader.ReadForSalesAsync(shop.Id, sales.Select(s => s.Id).ToArray(), cancellationToken)).ToDictionary(c => c.SaleId);
        var isLineItemLevel = level.Equals(SalesExportLevel.LineItems, StringComparison.OrdinalIgnoreCase);

        // Every return figure below comes from the adjustments posted in the period: a return is
        // reported where it was processed and a void where it was voided, never with the sale.
        var adjustments = await _returnAdjustmentReader.ReadAsync(shop.Id, startDate, endDate, sales, cancellationToken);
        var adjustmentsBySaleId = adjustments
            .GroupBy(a => a.SaleId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ReturnAdjustmentEntry>)g.ToList());

        var itemIds = sales
            .SelectMany(s => s.Items)
            .Where(i => i.ItemId.HasValue)
            .Select(i => i.ItemId!.Value)
            .Distinct()
            .ToList();
        var itemNameById = isLineItemLevel && itemIds.Count > 0
            ? (await _itemRepository.GetByIdsAsync(shop.Id, itemIds, cancellationToken)).ToDictionary(i => i.Id, i => i.Name)
            : new Dictionary<Guid, string>();

        var summaryRows = new List<SalesExportSummaryRowDto>();
        var lineItemRows = new List<SalesExportLineItemRowDto>();
        var taxBreakupMap = new Dictionary<decimal, TaxBreakupAccumulator>();

        foreach (var sale in sales)
        {
            var saleAdjustments = adjustmentsBySaleId.GetValueOrDefault(sale.Id, []);
            cancellationBySale.TryGetValue(sale.Id, out var cancellation);
            summaryRows.Add(BuildSummaryRow(sale, saleAdjustments) with { Status = sale.Status.ToString(), CancellationReference = cancellation?.ReferenceNumber, CancellationBusinessDate = cancellation?.BusinessDate });

            foreach (var saleItem in sale.Items)
            {
                var accumulator = Accumulator(taxBreakupMap, saleItem.TaxRatePercent);
                accumulator.SaleTaxableAmount += saleItem.TaxableAmount;
                accumulator.SaleTaxAmount += saleItem.TaxAmount;
            }

            if (isLineItemLevel)
            {
                // The physical return status of each line, as of now: how much of it has come back
                // under returns that still stand, whenever they were processed.
                var activeReturns = (await _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, cancellationToken))
                    .Where(r => !r.IsVoided)
                    .ToList();
                lineItemRows.AddRange(sale.Items.Select(saleItem => BuildLineItemRow(sale, saleItem, activeReturns, itemNameById) with { Status = sale.Status.ToString(), CancellationReference = cancellation?.ReferenceNumber, CancellationBusinessDate = cancellation?.BusinessDate }));
            }
        }

        foreach (var component in adjustments.SelectMany(a => a.TaxComponents))
        {
            var accumulator = Accumulator(taxBreakupMap, component.TaxRatePercent);
            accumulator.ReturnTaxableAmount += component.TaxableAmount;
            accumulator.ReturnTaxAmount += component.TaxAmount;
        }

        if (cancellations.Count > 0) Accumulator(taxBreakupMap, 0m).CancellationTaxableAmount = cancellations.Sum(c => c.RevenueAmount);

        var taxBreakup = taxBreakupMap.Values
            .Select(a => new SalesExportTaxBreakupDto(
                a.TaxRatePercent,
                a.SaleTaxableAmount,
                a.SaleTaxAmount,
                a.ReturnTaxableAmount,
                a.ReturnTaxAmount) { CancellationTaxableAmount = a.CancellationTaxableAmount })
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
            taxBreakup,
            adjustments) { CancellationAdjustments = cancellations };
    }

    private static SalesExportSummaryRowDto BuildSummaryRow(Sale sale, IReadOnlyList<ReturnAdjustmentEntry> adjustments)
    {
        var returnAmount = adjustments.Sum(a => a.FinancialAdjustment);
        var storeCredit = adjustments
            .SelectMany(a => a.Settlements)
            .Where(s => s.Kind == ReturnSettlementKind.StoreCreditIssued)
            .ToList();
        var storeCreditCodes = storeCredit
            .Select(s => s.CreditNoteCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct()
            .ToList();

        return new SalesExportSummaryRowDto(
            sale.InvoiceNumber,
            sale.SoldAt,
            sale.BusinessDate,
            sale.InvoiceDocument?.Buyer.Name ?? sale.CustomerName,
            sale.PaymentMethod.ToString(),
            sale.PaidAmount,
            sale.DueAmount,
            sale.TotalBeforeDiscount,
            sale.TotalDiscountAmount,
            sale.Items.Sum(i => i.TaxableAmount),
            sale.TotalTaxAmount,
            sale.TotalAmount,
            adjustments.Count == 0 ? null : string.Join(", ", adjustments.Select(a => a.Label)),
            returnAmount,
            adjustments.Sum(a => a.TaxableAdjustment ?? 0m),
            adjustments.Sum(a => a.TaxAdjustment ?? 0m),
            sale.TotalAmount - returnAmount,
            adjustments.Count > 0,
            sale.Items.Count,
            sale.CreditNoteAppliedAmount,
            storeCreditCodes.Count == 0 ? null : string.Join(", ", storeCreditCodes),
            storeCredit.Sum(s => s.Amount),
            sale.InvoiceDocument?.Totals.Components);
    }

    private static SalesExportLineItemRowDto BuildLineItemRow(
        Sale sale,
        SaleItem saleItem,
        IReadOnlyList<SaleReturn> activeReturns,
        IReadOnlyDictionary<Guid, string> itemNameById)
    {
        var returnedQuantity = activeReturns
            .SelectMany(r => r.Items)
            .Where(ri => ri.SaleItemId == saleItem.Id)
            .Sum(ri => ri.Quantity);
        var itemReturnNumbers = string.Join(", ", activeReturns
            .Where(r => r.Items.Any(ri => ri.SaleItemId == saleItem.Id))
            .Select(r => r.ReturnNumber));
        var lineName = !string.IsNullOrWhiteSpace(saleItem.LineName)
            ? saleItem.LineName
            : saleItem.ItemId.HasValue
                ? itemNameById.GetValueOrDefault(saleItem.ItemId.Value, "Unknown Item")
                : "Unknown Line";

        return new SalesExportLineItemRowDto(
            sale.InvoiceNumber,
            sale.SoldAt,
            sale.BusinessDate,
            sale.InvoiceDocument?.Buyer.Name ?? sale.CustomerName,
            lineName,
            saleItem.Quantity,
            saleItem.SalesPrice,
            saleItem.ItemDiscountAmount,
            saleItem.SaleDiscountAmount,
            saleItem.TaxRatePercent,
            saleItem.TaxableAmount,
            saleItem.TaxAmount,
            saleItem.TotalAmount,
            saleItem.IsPriceIncludingTax,
            returnedQuantity,
            GetReturnStatus(returnedQuantity, saleItem.Quantity),
            string.IsNullOrWhiteSpace(itemReturnNumbers) ? null : itemReturnNumbers,
            saleItem.LineType);
    }

    private static TaxBreakupAccumulator Accumulator(Dictionary<decimal, TaxBreakupAccumulator> map, decimal taxRatePercent)
    {
        if (!map.TryGetValue(taxRatePercent, out var accumulator))
        {
            accumulator = new TaxBreakupAccumulator { TaxRatePercent = taxRatePercent };
            map[taxRatePercent] = accumulator;
        }

        return accumulator;
    }

    private static string GetReturnStatus(decimal returnedQuantity, decimal soldQuantity)
    {
        if (returnedQuantity <= 0) return "NotReturned";
        return returnedQuantity >= soldQuantity ? "FullyReturned" : "PartiallyReturned";
    }

    private sealed class TaxBreakupAccumulator
    {
        public decimal TaxRatePercent { get; init; }
        public decimal SaleTaxableAmount { get; set; }
        public decimal SaleTaxAmount { get; set; }
        public decimal ReturnTaxableAmount { get; set; }
        public decimal ReturnTaxAmount { get; set; }
        public decimal CancellationTaxableAmount { get; set; }
    }
}
