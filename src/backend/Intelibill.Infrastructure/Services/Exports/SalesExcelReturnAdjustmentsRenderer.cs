using ClosedXML.Excel;
using Intelibill.Application.Features.Exports.Accounting;
using Intelibill.Application.Features.Exports.Sales.DTOs;
using Intelibill.Domain.Enums;

namespace Intelibill.Infrastructure.Services.Exports;

/// <summary>
/// One row per return adjustment posted in the period, with the original returned value beside the
/// financial adjustment and its settlement legs. Its totals are the period's return figures.
/// </summary>
internal static class SalesExcelReturnAdjustmentsRenderer
{
    internal const string SheetName = "Return Adjustments";

    private static readonly string[] Headers =
    [
        "Posting Date",
        "Entry",
        "Return #",
        "Invoice #",
        "Customer",
        "Original Returned Value",
        "Financial Adjustment",
        "Taxable Adjustment",
        "GST Adjustment",
        "Non-tax Adjustment",
        "Receivable Reduction",
        "Store Credit Issued",
        "External Refund",
        "Credit Note",
        "Accounting Status",
        "Policy",
        "Review Reason",
    ];

    // 1-based columns of the amounts, in Headers order.
    private const int FirstAmountColumn = 6;
    private const int LastAmountColumn = 13;

    internal static void Render(XLWorkbook workbook, SalesExportDatasetDto dataset)
    {
        var worksheet = workbook.Worksheets.Add(SheetName);
        var headerRow = SalesExcelExportRenderer.RenderMetadata(worksheet, dataset.Metadata, 1) + 2;

        for (var col = 1; col <= Headers.Length; col++)
        {
            var cell = worksheet.Cell(headerRow, col);
            cell.Value = Headers[col - 1];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#2D4A87");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        var firstDataRow = headerRow + 1;
        var row = firstDataRow;
        foreach (var entry in dataset.ReturnAdjustments)
        {
            RenderRow(worksheet, entry, row++);
        }

        var totalsRow = row + 1;
        worksheet.Cell(totalsRow, 1).Value = "TOTALS";
        worksheet.Cell(totalsRow, 1).Style.Font.Bold = true;
        for (var col = FirstAmountColumn; col <= LastAmountColumn; col++)
        {
            var cell = worksheet.Cell(totalsRow, col);
            if (dataset.ReturnAdjustments.Count > 0)
            {
                var letter = XLHelper.GetColumnLetterFromNumber(col);
                cell.FormulaA1 = $"SUM({letter}{firstDataRow}:{letter}{row - 1})";
            }
            else
            {
                cell.Value = 0m;
            }

            cell.Style.NumberFormat.Format = "#,##0.00";
            cell.Style.Font.Bold = true;
        }

        var totals = worksheet.Range(totalsRow, 1, totalsRow, Headers.Length);
        totals.Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF3FF");
        totals.Style.Border.OutsideBorder = XLBorderStyleValues.Thick;

        SalesExcelSummaryRenderer.ApplyPolishing(worksheet, Headers.Length, headerRow);
    }

    private static void RenderRow(IXLWorksheet worksheet, ReturnAdjustmentEntry entry, int row)
    {
        worksheet.Cell(row, 1).Value = entry.PostedAt.UtcDateTime;
        worksheet.Cell(row, 1).Style.DateFormat.Format = "yyyy-MM-dd";
        worksheet.Cell(row, 2).Value = entry.IsReversal ? "Void reversal" : "Return";
        worksheet.Cell(row, 3).Value = entry.ReturnNumber;
        worksheet.Cell(row, 4).Value = entry.InvoiceNumber;
        worksheet.Cell(row, 5).Value = entry.CustomerName ?? string.Empty;

        decimal?[] amounts =
        [
            entry.OriginalReturnedGross,
            entry.FinancialAdjustment,
            entry.TaxableAdjustment,
            entry.TaxAdjustment,
            entry.NonTaxAdjustment,
            entry.SettledBy(ReturnSettlementKind.ReceivableReduction),
            entry.SettledBy(ReturnSettlementKind.StoreCreditIssued),
            entry.SettledBy(ReturnSettlementKind.ExternalRefund),
        ];
        for (var i = 0; i < amounts.Length; i++)
        {
            var cell = worksheet.Cell(row, FirstAmountColumn + i);
            if (amounts[i] is { } amount)
            {
                cell.Value = amount;
                cell.Style.NumberFormat.Format = "#,##0.00";
            }
        }

        worksheet.Cell(row, 14).Value = string.Join(", ", entry.Settlements
            .Select(s => s.CreditNoteCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct());
        worksheet.Cell(row, 15).Value = SalesPdfDocument.StatusLabel(entry);
        worksheet.Cell(row, 16).Value = entry.PolicyVersion ?? string.Empty;
        worksheet.Cell(row, 17).Value = entry.ReviewReason ?? string.Empty;

        if (entry.Status == ReturnAccountingStatus.NeedsReview)
        {
            worksheet.Range(row, 1, row, Headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#FDECEA");
        }
    }
}
