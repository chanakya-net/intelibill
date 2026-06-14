using ClosedXML.Excel;
using Intelibill.Application.Features.Exports.Sales.DTOs;
using Intelibill.Infrastructure.Services.Exports;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Exports;

public sealed class SalesExcelExportRendererTests
{
    [Fact]
    public async Task RenderAsync_WithSummaryData_CreatesSalesSummaryWorkbook()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Cash", 1200m, 40m, 900m, 0m, 120m, 21.6m, 1161.6m, null, 0m, 0m, 0m, 1161.6m, false, 2),
            new("INV-002", new DateTimeOffset(2026, 5, 2, 11, 0, 0, TimeSpan.Zero), "Bob", "UPI", 400m, 20m, 700m, 0m, 300m, 54m, 754m, "R-001", 200m, 50m, 9m, 554m, true, 2)
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(5m, 120m, 6m, 0m, 0m),
            new(18m, 300m, 54m, 50m, 9m)
        };

        var dataset = new SalesExportDatasetDto(
            metadata,
            summaryRows,
            [],
            taxBreakup,
            []);

        var renderer = new SalesExcelExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);

        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", result.ContentType);
        Assert.NotEmpty(result.Content);

        using var stream = new MemoryStream(result.Content);
        using var workbook = new XLWorkbook(stream);

        var sheet = workbook.Worksheet("Sales Summary");
        Assert.NotNull(sheet);

        AssertMetadataValue(sheet, "Shop:", metadata.ShopName);
        AssertMetadataValue(sheet, "Period:", $"{metadata.StartDate:yyyy-MM-dd} to {metadata.EndDate:yyyy-MM-dd}");

        var headerRow = FindRow(sheet, "Invoice #");
        var totalsRow = FindRow(sheet, "TOTALS");
        var dataRow1 = headerRow + 1;
        var dataRow2 = dataRow1 + 1;

        Assert.Equal("INV-001", sheet.Cell(dataRow1, FindColumn(sheet, headerRow, "Invoice #")).GetString());
        Assert.Equal("Alice", sheet.Cell(dataRow1, FindColumn(sheet, headerRow, "Customer")).GetString());
        Assert.Equal("INV-002", sheet.Cell(dataRow2, FindColumn(sheet, headerRow, "Invoice #")).GetString());
        Assert.Equal("R-001", sheet.Cell(dataRow2, FindColumn(sheet, headerRow, "Return #")).GetString());

        var paidColumn = FindColumn(sheet, headerRow, "Paid Amount");
        var dueColumn = FindColumn(sheet, headerRow, "Due Amount");
        Assert.True(sheet.Cell(totalsRow, paidColumn).HasFormula);
        Assert.True(sheet.Cell(totalsRow, dueColumn).HasFormula);

        var rate5SalesTaxableColumn = FindColumn(sheet, headerRow, "Sales Taxable 5%");
        var rate5SalesTaxColumn = FindColumn(sheet, headerRow, "Sales GST 5%");
        var rate18ReturnTaxableColumn = FindColumn(sheet, headerRow, "Return Taxable 18%");
        var rate18ReturnTaxColumn = FindColumn(sheet, headerRow, "Return GST 18%");

        Assert.Equal(120m, sheet.Cell(totalsRow, rate5SalesTaxableColumn).GetValue<decimal>());
        Assert.Equal(6m, sheet.Cell(totalsRow, rate5SalesTaxColumn).GetValue<decimal>());
        Assert.Equal(50m, sheet.Cell(totalsRow, rate18ReturnTaxableColumn).GetValue<decimal>());
        Assert.Equal(9m, sheet.Cell(totalsRow, rate18ReturnTaxColumn).GetValue<decimal>());
    }

    [Fact]
    public async Task RenderAsync_WithCreditNoteFields_ContainsCreditNoteColumns()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new(
                "INV-001",
                new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero),
                "Alice",
                "Cash",
                1200m,
                40m,
                900m,
                0m,
                120m,
                21.6m,
                1061.6m,
                null,
                0m,
                0m,
                0m,
                1061.6m,
                false,
                2,
                200m,
                "CN-001",
                200m)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, [], [], []);
        var renderer = new SalesExcelExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);

        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", result.ContentType);
        Assert.NotEmpty(result.Content);

        using var stream = new MemoryStream(result.Content);
        using var workbook = new XLWorkbook(stream);

        var sheet = workbook.Worksheet("Sales Summary");
        Assert.NotNull(sheet);

        var headerRow = FindRow(sheet, "Invoice #");
        var totalsRow = FindRow(sheet, "TOTALS");
        var dataRow = headerRow + 1;

        var creditNoteAppliedColumn = FindColumn(sheet, headerRow, "Credit Note Applied");
        var issuedCreditNoteColumn = FindColumn(sheet, headerRow, "Credit Note Issued");
        var issuedAmountColumn = FindColumn(sheet, headerRow, "Credit Note Issued Amount");

        Assert.Equal(200m, sheet.Cell(dataRow, creditNoteAppliedColumn).GetValue<decimal>());
        Assert.Equal("CN-001", sheet.Cell(dataRow, issuedCreditNoteColumn).GetString());
        Assert.Equal(200m, sheet.Cell(dataRow, issuedAmountColumn).GetValue<decimal>());

        Assert.True(sheet.Cell(totalsRow, creditNoteAppliedColumn).HasFormula);
        Assert.True(sheet.Cell(totalsRow, issuedAmountColumn).HasFormula);
    }

    [Fact]
    public async Task RenderAsync_WithLineItemData_CreatesLineItemsWorkbook()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "lineItems");

        var lineItemRows = new List<SalesExportLineItemRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Tomato", 10m, 50m, 3m, 2m, 5m, 500m, 25m, 520m, false, 0m, null, null),
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Potato", 20m, 30m, 0m, 0m, 5m, 600m, 30m, 630m, false, 5m, "Partial", "RET-001"),
            new("INV-002", new DateTimeOffset(2026, 5, 2, 11, 0, 0, TimeSpan.Zero), "Bob", "Onion", 15m, 40m, 6m, 4m, 18m, 450m, 81m, 541m, true, 0m, null, null)
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(5m, 1100m, 55m, 0m, 0m),
            new(18m, 450m, 81m, 0m, 0m)
        };

        var dataset = new SalesExportDatasetDto(
            metadata,
            [],
            lineItemRows,
            taxBreakup,
            []);

        var renderer = new SalesExcelExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);

        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", result.ContentType);
        Assert.NotEmpty(result.Content);

        using var stream = new MemoryStream(result.Content);
        using var workbook = new XLWorkbook(stream);

        var sheet = workbook.Worksheet("Sales Line Items");
        Assert.NotNull(sheet);

        AssertMetadataValue(sheet, "Shop:", metadata.ShopName);
        AssertMetadataValue(sheet, "Level:", "lineItems");

        var headerRow = FindRow(sheet, "Invoice #");
        var totalsRow = FindRow(sheet, "TOTALS");
        var dataRow1 = headerRow + 1;
        var dataRow2 = dataRow1 + 1;
        var dataRow3 = dataRow2 + 1;

        Assert.Equal("INV-001", sheet.Cell(dataRow1, FindColumn(sheet, headerRow, "Invoice #")).GetString());
        Assert.Equal(new DateTime(2026, 5, 1), sheet.Cell(dataRow1, FindColumn(sheet, headerRow, "Date")).GetDateTime().Date);
        Assert.Equal("Alice", sheet.Cell(dataRow1, FindColumn(sheet, headerRow, "Customer")).GetString());
        Assert.Equal("Tomato", sheet.Cell(dataRow1, FindColumn(sheet, headerRow, "Item Name")).GetString());

        var quantityColumn = FindColumn(sheet, headerRow, "Quantity");
        var salesPriceColumn = FindColumn(sheet, headerRow, "Sales Price");
        var taxRatePercentColumn = FindColumn(sheet, headerRow, "Tax Rate %");
        var taxAmountColumn = FindColumn(sheet, headerRow, "Tax Amount");
        var lineTotalColumn = FindColumn(sheet, headerRow, "Line Total");
        var returnedQuantityColumn = FindColumn(sheet, headerRow, "Returned Quantity");
        var returnStatusColumn = FindColumn(sheet, headerRow, "Return Status");

        Assert.Equal(10m, sheet.Cell(dataRow1, quantityColumn).GetValue<decimal>());
        Assert.Equal(50m, sheet.Cell(dataRow1, salesPriceColumn).GetValue<decimal>());
        Assert.Equal(5m, sheet.Cell(dataRow1, taxRatePercentColumn).GetValue<decimal>());
        Assert.Equal(25m, sheet.Cell(dataRow1, taxAmountColumn).GetValue<decimal>());
        Assert.Equal(520m, sheet.Cell(dataRow1, lineTotalColumn).GetValue<decimal>());
        Assert.Equal(0m, sheet.Cell(dataRow1, returnedQuantityColumn).GetValue<decimal>());

        Assert.Equal(5m, sheet.Cell(dataRow2, returnedQuantityColumn).GetValue<decimal>());
        Assert.Equal("Partial", sheet.Cell(dataRow2, returnStatusColumn).GetString());

        Assert.True(sheet.Cell(totalsRow, quantityColumn).HasFormula);
        Assert.True(sheet.Cell(totalsRow, taxAmountColumn).HasFormula);
        Assert.True(sheet.Cell(totalsRow, lineTotalColumn).HasFormula);
    }

    private static int FindRow(IXLWorksheet sheet, string label)
    {
        return sheet.RowsUsed().Select(r => r.RowNumber())
            .First(row => sheet.Cell(row, 1).GetString() == label);
    }

    private static int FindColumn(IXLWorksheet sheet, int row, string header)
    {
        for (var col = 1; col <= sheet.ColumnCount(); col++)
        {
            if (sheet.Cell(row, col).GetString() == header)
            {
                return col;
            }
        }

        return 0;
    }

    private static void AssertMetadataValue(IXLWorksheet sheet, string label, string value)
    {
        var row = FindRow(sheet, label);
        Assert.Equal(value, sheet.Cell(row, 2).GetString());
    }
}
