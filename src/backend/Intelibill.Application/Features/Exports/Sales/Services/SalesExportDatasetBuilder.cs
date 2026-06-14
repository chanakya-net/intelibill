using Intelibill.Application.Features.Exports.Sales.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Exports.Sales.Services;

public sealed class SalesExportDatasetBuilder : ISalesExportDatasetBuilder
{
    private readonly ISaleRepository _saleRepository;
    private readonly ISaleReturnRepository _saleReturnRepository;
    private readonly ICreditNoteRepository _creditNoteRepository;
    private readonly IItemRepository _itemRepository;

    public SalesExportDatasetBuilder(
        ISaleRepository saleRepository,
        ISaleReturnRepository saleReturnRepository,
        ICreditNoteRepository creditNoteRepository,
        IItemRepository itemRepository)
    {
        _saleRepository = saleRepository;
        _saleReturnRepository = saleReturnRepository;
        _creditNoteRepository = creditNoteRepository;
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
        
        var itemIds = sales
            .SelectMany(s => s.Items)
            .Where(i => i.ItemId.HasValue)
            .Select(i => i.ItemId!.Value)
            .Distinct()
            .ToList();
        var items = await _itemRepository.GetByIdsAsync(shop.Id, itemIds, cancellationToken);
        var itemNameById = items.ToDictionary(i => i.Id, i => i.Name);

        var summaryRows = new List<SalesExportSummaryRowDto>();
        var lineItemRows = new List<SalesExportLineItemRowDto>();
        var returnRows = new List<SalesExportReturnRowDto>();
        var taxBreakupMap = new Dictionary<decimal, TaxBreakupAccumulator>();

        foreach (var sale in sales)
        {
            var allReturns = await _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, cancellationToken);
            var returns = allReturns
                .Where(r => !r.IsVoided)
                .ToList();

            var returnIds = allReturns
                .Select(r => r.Id)
                .Distinct()
                .ToList();
            var creditNotesByReturnId = returnIds.Count == 0
                ? new List<CreditNote>()
                : await _creditNoteRepository.GetByReturnIdsAsync(shop.Id, returnIds, cancellationToken);
            var creditNotesByReturnIdLookup = creditNotesByReturnId
                .GroupBy(note => note.SaleReturnId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<CreditNote>)group
                        .OrderBy(note => note.Code)
                        .ToList());

            var returnNumbers = string.Join(", ", returns.Select(r => r.ReturnNumber));
            var totalReturnAmount = returns.Sum(r => r.TotalRefundAmount);
            var totalReturnTaxable = returns.Sum(r => r.TotalTaxableAmount);
            var totalReturnTax = returns.Sum(r => r.TotalTaxAmount);

            var issuedCreditNotes = allReturns
                .SelectMany(returnRecord =>
                    GetCreditNotesForReturn(creditNotesByReturnIdLookup, returnRecord.Id))
                .ToList();
            var issuedCreditNoteCodes = issuedCreditNotes.Count == 0
                ? null
                : string.Join(", ", issuedCreditNotes.Select(note => note.Code));
            var issuedCreditNoteAmount = issuedCreditNotes.Sum(note => note.OriginalAmount);

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
                sale.Items.Count,
                sale.CreditNoteAppliedAmount,
                issuedCreditNoteCodes,
                issuedCreditNoteAmount));

            // Collect return rows for credit notes (including voided for renderer-level filtering)
            foreach (var returnRecord in allReturns)
            {
                var returnTaxBreakup = returnRecord.Items
                    .GroupBy(ri => ri.OriginalTaxRatePercent)
                    .Select(g => new SalesExportReturnTaxBreakupDto(
                        g.Key,
                        g.Sum(ri => ri.TaxableAmount),
                        g.Sum(ri => ri.TaxAmount)))
                    .ToList();

                returnRows.Add(new SalesExportReturnRowDto(
                    returnRecord.ReturnNumber,
                    returnRecord.ProcessedAt,
                    sale.InvoiceNumber,
                    sale.CustomerName,
                    returnRecord.TotalRefundAmount,
                    returnRecord.TotalTaxableAmount,
                    returnRecord.TotalTaxAmount,
                    returnTaxBreakup,
                    returnRecord.IsVoided,
                    GetCreditNoteCodes(creditNotesByReturnIdLookup, returnRecord.Id),
                    GetCreditNoteAmount(creditNotesByReturnIdLookup, returnRecord.Id),
                    GetCreditNoteRemainingBalance(creditNotesByReturnIdLookup, returnRecord.Id)));
            }

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
                    var lineName = !string.IsNullOrWhiteSpace(saleItem.LineName)
                        ? saleItem.LineName
                        : saleItem.ItemId.HasValue
                            ? itemNameById.GetValueOrDefault(saleItem.ItemId.Value, "Unknown Item")
                            : "Unknown Line";

                    lineItemRows.Add(new SalesExportLineItemRowDto(
                        sale.InvoiceNumber,
                        sale.SoldAt,
                        sale.CustomerName,
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
                        saleItem.LineType));
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
            taxBreakup,
            returnRows);
    }

    private static string GetReturnStatus(decimal returnedQuantity, decimal soldQuantity)
    {
        if (returnedQuantity <= 0) return "NotReturned";
        return returnedQuantity >= soldQuantity ? "FullyReturned" : "PartiallyReturned";
    }

    private static string? GetCreditNoteCodes(
        Dictionary<Guid, IReadOnlyList<CreditNote>> creditNotesByReturnIdLookup,
        Guid returnId)
    {
        var noteCodes = GetCreditNotesForReturn(creditNotesByReturnIdLookup, returnId)
            .Select(note => note.Code)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToList();

        return noteCodes.Count == 0
            ? null
            : string.Join(", ", noteCodes);
    }

    private static decimal GetCreditNoteAmount(
        Dictionary<Guid, IReadOnlyList<CreditNote>> creditNotesByReturnIdLookup,
        Guid returnId) =>
        GetCreditNotesForReturn(creditNotesByReturnIdLookup, returnId)
            .Sum(note => note.OriginalAmount);

    private static decimal GetCreditNoteRemainingBalance(
        Dictionary<Guid, IReadOnlyList<CreditNote>> creditNotesByReturnIdLookup,
        Guid returnId) =>
        GetCreditNotesForReturn(creditNotesByReturnIdLookup, returnId)
            .Sum(note => note.AvailableBalance);

    private static IReadOnlyList<CreditNote> GetCreditNotesForReturn(
        Dictionary<Guid, IReadOnlyList<CreditNote>> creditNotesByReturnIdLookup,
        Guid returnId)
    {
        return creditNotesByReturnIdLookup.GetValueOrDefault(returnId, Array.Empty<CreditNote>());
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
