using ClosedXML.Excel;
using Intelibill.Application.Features.Exports.Sales.DTOs;

namespace Intelibill.Infrastructure.Services.Exports;

internal static class SalesExcelCancellationRenderer
{
    internal static void Render(XLWorkbook workbook, SalesExportDatasetDto dataset)
    {
        if (dataset.CancellationAdjustments.Count == 0) return;
        var sheet = workbook.Worksheets.Add("Sale Cancellations");
        var headers = new[] { "Reference", "Date", "Original invoice", "Original date", "Original seller", "Original buyer", "Reason", "Revenue reversal", "COGS reversal", "Due reduction", "Confirmed cash refund", "Description", "Recovered quantity", "UQC", "Line recorded cost", "Disclaimer" };
        for (var col = 0; col < headers.Length; col++) sheet.Cell(1, col + 1).Value = headers[col];
        var row = 2;
        foreach (var entry in dataset.CancellationAdjustments)
        {
            sheet.Cell(row, 1).Value = entry.ReferenceNumber;
            sheet.Cell(row, 2).Value = entry.BusinessDate.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 3).Value = entry.InvoiceNumber;
            sheet.Cell(row, 4).Value = entry.InvoiceBusinessDate.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 5).Value = entry.Seller.LegalName;
            sheet.Cell(row, 6).Value = entry.CustomerName;
            sheet.Cell(row, 7).Value = entry.Reason;
            sheet.Cell(row, 8).Value = -entry.RevenueAmount;
            sheet.Cell(row, 9).Value = -entry.RecordedCogs;
            sheet.Cell(row, 10).Value = entry.DueReductionAmount;
            sheet.Cell(row, 11).Value = entry.CashRefundAmount;
            sheet.Cell(row, 16).Value = entry.Disclaimer;
            sheet.Cell(row, 2).Style.DateFormat.Format = "yyyy-MM-dd";
            sheet.Cell(row, 4).Style.DateFormat.Format = "yyyy-MM-dd";
            row++;
            foreach (var line in entry.Lines)
            {
                sheet.Cell(row, 1).Value = entry.ReferenceNumber;
                sheet.Cell(row, 12).Value = line.Description;
                sheet.Cell(row, 13).Value = line.Quantity;
                sheet.Cell(row, 14).Value = line.Uqc;
                sheet.Cell(row, 15).Value = line.RecordedCostAmount;
                row++;
            }
        }
        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, 60);
        sheet.Column(13).Style.NumberFormat.Format = "0.###";
        sheet.Columns(8, 11).Style.NumberFormat.Format = "#,##0.00";
        sheet.Column(15).Style.NumberFormat.Format = "#,##0.00";

        var totals = workbook.Worksheets.Add("Period Totals");
        totals.Cell(1, 1).Value = "Original invoices";
        totals.Cell(1, 2).Value = dataset.SummaryRows.Count;
        totals.Cell(2, 1).Value = "Issued sales";
        totals.Cell(2, 2).Value = dataset.SummaryRows.Sum(r => r.TotalAmount);
        totals.Cell(3, 1).Value = "Ordinary return adjustments";
        totals.Cell(3, 2).Value = dataset.ReturnAdjustments.Sum(r => r.FinancialAdjustment);
        totals.Cell(4, 1).Value = "Cancellations";
        totals.Cell(4, 2).Value = dataset.CancellationAdjustments.Sum(c => c.TotalAmount);
        totals.Cell(5, 1).Value = "Net sales";
        totals.Cell(5, 2).Value = dataset.SummaryRows.Sum(r => r.TotalAmount) - dataset.ReturnAdjustments.Sum(r => r.FinancialAdjustment) - dataset.CancellationAdjustments.Sum(c => c.TotalAmount);
        totals.Cell(6, 1).Value = "Net taxable revenue";
        totals.Cell(6, 2).Value = dataset.TaxBreakup.Sum(t => t.NetTaxableAmount);
        totals.Cell(7, 1).Value = "Net tax";
        totals.Cell(7, 2).Value = dataset.TaxBreakup.Sum(t => t.NetTaxAmount);
        totals.Column(2).Style.NumberFormat.Format = "#,##0.00";
        totals.Columns().AdjustToContents();
    }
}
