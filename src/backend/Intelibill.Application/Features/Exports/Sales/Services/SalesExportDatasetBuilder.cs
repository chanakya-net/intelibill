using Intelibill.Application.Features.Exports.Accounting;
using Intelibill.Application.Features.Exports.Sales.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Exports.Sales.Services;

public sealed class SalesExportDatasetBuilder : ISalesExportDatasetBuilder
{
    public async Task<SalesExportDatasetDto> BuildAsync(
        IExportReadSnapshot snapshot, Shop shop, User generatedBy, DateOnly startDate, DateOnly endDate,
        string level, CancellationToken cancellationToken)
    {
        List<SalesExportSummaryRowDto> summaries = [];
        List<SalesExportLineItemRowDto> lines = [];
        List<SalesExportTaxBreakupDto> taxes = [];
        List<ReturnAdjustmentEntry> returns = [];
        List<SaleCancellationAdjustmentEntry> cancellations = [];
        ExportCursor? cursor = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await snapshot.ReadPageAsync(shop.Id, new(startDate, endDate), level, cursor, ExportReadLimits.PageSize, cancellationToken);
            summaries.AddRange(page.SummaryRows);
            lines.AddRange(page.LineItemRows);
            taxes.AddRange(page.TaxBreakup);
            returns.AddRange(page.ReturnAdjustments);
            cancellations.AddRange(page.CancellationAdjustments);
            if (page.IsComplete) break;
            cursor = page.NextCursor ?? throw new InvalidOperationException("An incomplete export page requires a cursor.");
        } while (true);

        returns = returns.OrderBy(a => a.PostedAt).ThenBy(a => a.ReturnNumber, StringComparer.Ordinal).ThenBy(a => a.Kind).ThenBy(a => a.SaleReturnId).ToList();
        var byInvoice = returns.GroupBy(a => a.InvoiceNumber).ToDictionary(g => g.Key, g => g.ToList());
        for (var index = 0; index < summaries.Count; index++)
        {
            var row = summaries[index];
            if (!byInvoice.TryGetValue(row.InvoiceNumber, out var adjustments)) continue;
            var credits = adjustments.SelectMany(a => a.Settlements).Where(s => s.Kind == ReturnSettlementKind.StoreCreditIssued).ToList();
            var codes = credits.Select(s => s.CreditNoteCode).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
            summaries[index] = row with
            {
                ReturnNumbers = string.Join(", ", adjustments.Select(a => a.Label)),
                ReturnAmount = adjustments.Sum(a => a.FinancialAdjustment),
                ReturnTaxableAmount = adjustments.Sum(a => a.TaxableAdjustment ?? 0m),
                ReturnTaxAmount = adjustments.Sum(a => a.TaxAdjustment ?? 0m),
                NetSalesAmount = row.TotalAmount - adjustments.Sum(a => a.FinancialAdjustment),
                HasReturns = true,
                IssuedCreditNoteCodes = codes.Count == 0 ? null : string.Join(", ", codes),
                IssuedCreditNoteAmount = credits.Sum(s => s.Amount)
            };
        }
        var taxBreakup = taxes.GroupBy(t => t.TaxRatePercent).OrderBy(g => g.Key)
            .Select(g => new SalesExportTaxBreakupDto(g.Key, g.Sum(t => t.SaleTaxableAmount), g.Sum(t => t.SaleTaxAmount),
                g.Sum(t => t.ReturnTaxableAmount), g.Sum(t => t.ReturnTaxAmount)) { CancellationTaxableAmount = g.Sum(t => t.CancellationTaxableAmount) }).ToList();
        var metadata = new SalesExportMetadataDto(shop.Name, $"{shop.Address}, {shop.City}, {shop.State} - {shop.Pincode}", shop.GstNumber,
            $"{generatedBy.FirstName} {generatedBy.LastName}".Trim(), DateTimeOffset.UtcNow, startDate, endDate, level);
        return new(metadata, summaries, lines, taxBreakup, returns) { CancellationAdjustments = cancellations };
    }
}
