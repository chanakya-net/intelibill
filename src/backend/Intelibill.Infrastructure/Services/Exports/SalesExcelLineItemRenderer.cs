using ClosedXML.Excel;

using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Application.Features.Exports.Sales.DTOs;

namespace Intelibill.Infrastructure.Services.Exports;

internal static class SalesExcelLineItemRenderer
{
    internal static Task<SalesExportResult> RenderLineItemsAsync(XLWorkbook workbook, SalesExportDatasetDto dataset)
    {
        var worksheet = workbook.Worksheets.Add("Sales Line Items");

        var currentRow = 1;
        currentRow = SalesExcelExportRenderer.RenderMetadata(worksheet, dataset.Metadata, currentRow);

        currentRow++;

        var headerRow = currentRow + 1;
        var columnPlan = RenderLineItemHeaders(worksheet, headerRow);
        var firstDataRow = headerRow + 1;

        var dataRow = firstDataRow;
        foreach (var row in dataset.LineItemRows)
        {
            RenderLineItemRow(worksheet, row, columnPlan, dataRow);
            dataRow++;
        }

        var totalsRow = dataRow + 1;
        RenderLineItemTotalsRow(worksheet, dataset.LineItemRows, columnPlan, firstDataRow, totalsRow);

        ApplyLineItemPolishing(worksheet, columnPlan.TotalColumnsEnd, headerRow);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var content = stream.ToArray();

        return Task.FromResult(new SalesExportResult(
            content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "sales-export.xlsx"));
    }

    internal static LineItemColumnPlan RenderLineItemHeaders(IXLWorksheet worksheet, int startRow)
    {
        var col = 1;
        var plan = new LineItemColumnPlan();

        worksheet.Cell(startRow, col).Value = "Invoice #";
        plan.InvoiceNumberColumn = col++;

        worksheet.Cell(startRow, col).Value = "Date";
        plan.DateColumn = col++;

        worksheet.Cell(startRow, col).Value = "Customer";
        plan.CustomerColumn = col++;

        worksheet.Cell(startRow, col).Value = "Item Name";
        plan.ItemNameColumn = col++;

        worksheet.Cell(startRow, col).Value = "Quantity";
        plan.QuantityColumn = col++;
        plan.TotalFormulaColumns.Add(plan.QuantityColumn);

        worksheet.Cell(startRow, col).Value = "Sales Price";
        plan.SalesPriceColumn = col;
        col++;

        worksheet.Cell(startRow, col).Value = "Item Discount";
        plan.ItemDiscountColumn = col;
        plan.TotalFormulaColumns.Add(col++);

        worksheet.Cell(startRow, col).Value = "Sale Discount";
        plan.SaleDiscountColumn = col;
        plan.TotalFormulaColumns.Add(col++);

        worksheet.Cell(startRow, col).Value = "Tax Rate %";
        plan.TaxRatePercentColumn = col++;

        worksheet.Cell(startRow, col).Value = "Taxable Amount";
        plan.TaxableAmountColumn = col;
        plan.TotalFormulaColumns.Add(col++);

        worksheet.Cell(startRow, col).Value = "Tax Amount";
        plan.TaxAmountColumn = col;
        plan.TotalFormulaColumns.Add(col++);

        worksheet.Cell(startRow, col).Value = "Line Total";
        plan.LineTotalColumn = col;
        plan.TotalFormulaColumns.Add(col++);

        worksheet.Cell(startRow, col).Value = "Inclusive Tax";
        plan.InclusiveTaxColumn = col++;

        worksheet.Cell(startRow, col).Value = "Returned Quantity";
        plan.ReturnedQuantityColumn = col++;

        worksheet.Cell(startRow, col).Value = "Return Status";
        plan.ReturnStatusColumn = col++;

        worksheet.Cell(startRow, col).Value = "Return Numbers";
        plan.ReturnNumbersColumn = col++;

        plan.HeaderRow = startRow;
        plan.TotalColumnsEnd = col - 1;

        for (var i = 1; i <= plan.TotalColumnsEnd; i++)
        {
            worksheet.Cell(startRow, i).Style.Font.Bold = true;
            worksheet.Cell(startRow, i).Style.Font.FontColor = XLColor.White;
            worksheet.Cell(startRow, i).Style.Fill.BackgroundColor = XLColor.FromHtml("#2D4A87");
            worksheet.Cell(startRow, i).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        return plan;
    }

    internal static void RenderLineItemRow(
        IXLWorksheet worksheet,
        SalesExportLineItemRowDto row,
        LineItemColumnPlan columnPlan,
        int rowNum)
    {
        worksheet.Cell(rowNum, columnPlan.InvoiceNumberColumn).Value = row.InvoiceNumber;
        worksheet.Cell(rowNum, columnPlan.DateColumn).Value = row.SaleDate.UtcDateTime;
        worksheet.Cell(rowNum, columnPlan.DateColumn).Style.DateFormat.Format = "yyyy-MM-dd";
        worksheet.Cell(rowNum, columnPlan.CustomerColumn).Value = row.CustomerName ?? string.Empty;
        worksheet.Cell(rowNum, columnPlan.ItemNameColumn).Value = row.ItemName;
        worksheet.Cell(rowNum, columnPlan.QuantityColumn).Value = row.SalesQuantity;
        worksheet.Cell(rowNum, columnPlan.QuantityColumn).Style.NumberFormat.Format = "#,##0.00";

        worksheet.Cell(rowNum, columnPlan.SalesPriceColumn).Value = row.SalesPrice;
        worksheet.Cell(rowNum, columnPlan.SalesPriceColumn).Style.NumberFormat.Format = "#,##0.00";

        worksheet.Cell(rowNum, columnPlan.ItemDiscountColumn).Value = row.ItemDiscountAmount;
        worksheet.Cell(rowNum, columnPlan.ItemDiscountColumn).Style.NumberFormat.Format = "#,##0.00";

        worksheet.Cell(rowNum, columnPlan.SaleDiscountColumn).Value = row.SaleDiscountAmount;
        worksheet.Cell(rowNum, columnPlan.SaleDiscountColumn).Style.NumberFormat.Format = "#,##0.00";

        worksheet.Cell(rowNum, columnPlan.TaxRatePercentColumn).Value = row.TaxRatePercent;
        worksheet.Cell(rowNum, columnPlan.TaxRatePercentColumn).Style.NumberFormat.Format = "0.##\"%\"";

        worksheet.Cell(rowNum, columnPlan.TaxableAmountColumn).Value = row.TaxableAmount;
        worksheet.Cell(rowNum, columnPlan.TaxableAmountColumn).Style.NumberFormat.Format = "#,##0.00";

        worksheet.Cell(rowNum, columnPlan.TaxAmountColumn).Value = row.TaxAmount;
        worksheet.Cell(rowNum, columnPlan.TaxAmountColumn).Style.NumberFormat.Format = "#,##0.00";

        worksheet.Cell(rowNum, columnPlan.LineTotalColumn).Value = row.LineTotal;
        worksheet.Cell(rowNum, columnPlan.LineTotalColumn).Style.NumberFormat.Format = "#,##0.00";

        worksheet.Cell(rowNum, columnPlan.InclusiveTaxColumn).Value = row.IsPriceIncludingTax ? "Yes" : "No";

        worksheet.Cell(rowNum, columnPlan.ReturnedQuantityColumn).Value = row.ReturnedQuantity;
        worksheet.Cell(rowNum, columnPlan.ReturnedQuantityColumn).Style.NumberFormat.Format = "#,##0.00";

        worksheet.Cell(rowNum, columnPlan.ReturnStatusColumn).Value = row.ReturnStatus ?? string.Empty;
        worksheet.Cell(rowNum, columnPlan.ReturnNumbersColumn).Value = row.ReturnNumbers ?? string.Empty;
    }

    internal static void RenderLineItemTotalsRow(
        IXLWorksheet worksheet,
        IReadOnlyList<SalesExportLineItemRowDto> lineItemRows,
        LineItemColumnPlan columnPlan,
        int firstDataRow,
        int totalsRow)
    {
        var lastDataRow = Math.Max(firstDataRow, totalsRow - 1);
        var hasData = lineItemRows.Count > 0;

        worksheet.Cell(totalsRow, 1).Value = "TOTALS";
        worksheet.Cell(totalsRow, 1).Style.Font.Bold = true;

        foreach (var col in columnPlan.TotalFormulaColumns)
        {
            if (hasData)
            {
                var columnLetter = XLHelper.GetColumnLetterFromNumber(col);
                worksheet.Cell(totalsRow, col).FormulaA1 = $"SUM({columnLetter}{firstDataRow}:{columnLetter}{lastDataRow})";
            }
            else
            {
                worksheet.Cell(totalsRow, col).Value = 0m;
            }

            worksheet.Cell(totalsRow, col).Style.NumberFormat.Format = "#,##0.00";
            worksheet.Cell(totalsRow, col).Style.Font.Bold = true;
        }

        var range = worksheet.Range(totalsRow, 1, totalsRow, columnPlan.TotalColumnsEnd);
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF3FF");
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thick;
    }

    internal static void ApplyLineItemPolishing(IXLWorksheet worksheet, int totalColumnsEnd, int headerRow)
    {
        worksheet.SheetView.FreezeRows(headerRow);
        worksheet.Range(headerRow, 1, headerRow, totalColumnsEnd).SetAutoFilter();
        worksheet.Columns(1, totalColumnsEnd).AdjustToContents();

        for (var col = 1; col <= totalColumnsEnd; col++)
        {
            if (worksheet.Column(col).Width < 12)
            {
                worksheet.Column(col).Width = 12;
            }
        }

        worksheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }
}

internal sealed class LineItemColumnPlan
{
    public LineItemColumnPlan()
    {
        TotalFormulaColumns = new List<int>();
    }

    public int HeaderRow { get; set; }
    public int InvoiceNumberColumn { get; set; }
    public int DateColumn { get; set; }
    public int CustomerColumn { get; set; }
    public int ItemNameColumn { get; set; }
    public int QuantityColumn { get; set; }
    public int SalesPriceColumn { get; set; }
    public int ItemDiscountColumn { get; set; }
    public int SaleDiscountColumn { get; set; }
    public int TaxRatePercentColumn { get; set; }
    public int TaxableAmountColumn { get; set; }
    public int TaxAmountColumn { get; set; }
    public int LineTotalColumn { get; set; }
    public int InclusiveTaxColumn { get; set; }
    public int ReturnedQuantityColumn { get; set; }
    public int ReturnStatusColumn { get; set; }
    public int ReturnNumbersColumn { get; set; }
    public int TotalColumnsEnd { get; set; }
    public List<int> TotalFormulaColumns { get; }
}
