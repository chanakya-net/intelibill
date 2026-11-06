using ClosedXML.Excel;
using Intelibill.Application.Features.Exports.Accounting;
using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Application.Features.Exports.Sales.DTOs;
using Intelibill.Domain.Enums;
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
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), new DateOnly(2026, 5, 1), "Alice", "Cash", 1200m, 40m, 900m, 0m, 120m, 21.6m, 1161.6m, null, 0m, 0m, 0m, 1161.6m, false, 2),
            new("INV-002", new DateTimeOffset(2026, 5, 2, 11, 0, 0, TimeSpan.Zero), new DateOnly(2026, 5, 2), "Bob", "UPI", 400m, 20m, 700m, 0m, 300m, 54m, 754m, "R-001", 200m, 50m, 9m, 554m, true, 2)
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
                new DateOnly(2026, 5, 1),
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
        var paidAmountColumn = FindColumn(sheet, headerRow, "Paid Amount");
        var dueAmountColumn = FindColumn(sheet, headerRow, "Due Amount");

        Assert.Equal(1200m, sheet.Cell(dataRow, paidAmountColumn).GetValue<decimal>());
        Assert.Equal(40m, sheet.Cell(dataRow, dueAmountColumn).GetValue<decimal>());
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
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), new DateOnly(2026, 5, 1), "Alice", "Tomato", 10m, 50m, 3m, 2m, 5m, 500m, 25m, 520m, false, 0m, null, null),
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), new DateOnly(2026, 5, 1), "Alice", "Potato", 20m, 30m, 0m, 0m, 5m, 600m, 30m, 630m, false, 5m, "Partial", "RET-001"),
            new("INV-002", new DateTimeOffset(2026, 5, 2, 11, 0, 0, TimeSpan.Zero), new DateOnly(2026, 5, 2), "Bob", "Onion", 15m, 40m, 6m, 4m, 18m, 450m, 81m, 541m, true, 0m, null, null)
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

    [Fact]
    public async Task RenderAsync_ReturnAdjustmentsSheet_ListsEachPostingWithSettlementSplitAndTotals()
    {
        var mixed = Adjustment("RET-1", "INV-001", 100m, 84.75m, 15.25m,
            new ReturnAdjustmentSettlement(ReturnSettlementKind.ReceivableReduction, 40m, null, null, null),
            new ReturnAdjustmentSettlement(ReturnSettlementKind.StoreCreditIssued, 60m, null, Guid.NewGuid(), "CN-0001"));
        var zero = Adjustment("RET-2", "INV-001", 0m, 0m, 0m);
        var reversal = Adjustment("RET-0", "INV-000", -50m, -42.37m, -7.63m,
            new ReturnAdjustmentSettlement(ReturnSettlementKind.ExternalRefund, -50m, null, null, null)) with
        {
            Kind = ReturnAdjustmentEntryKind.VoidReversal,
        };

        var sheet = await RenderAdjustmentsAsync([mixed, zero, reversal]);

        var headerRow = FindRow(sheet, "Posting Date");
        var totalsRow = FindRow(sheet, "TOTALS");
        Assert.Equal(headerRow + 5, totalsRow);
        var original = FindColumn(sheet, headerRow, "Original Returned Value");
        var financial = FindColumn(sheet, headerRow, "Financial Adjustment");
        var receivable = FindColumn(sheet, headerRow, "Receivable Reduction");
        var storeCredit = FindColumn(sheet, headerRow, "Store Credit Issued");
        var refund = FindColumn(sheet, headerRow, "External Refund");
        var gst = FindColumn(sheet, headerRow, "GST Adjustment");

        Assert.Equal("Return", sheet.Cell(headerRow + 1, 2).GetString());
        Assert.Equal(118m, sheet.Cell(headerRow + 1, original).GetValue<decimal>());
        Assert.Equal(40m, sheet.Cell(headerRow + 1, receivable).GetValue<decimal>());
        Assert.Equal(60m, sheet.Cell(headerRow + 1, storeCredit).GetValue<decimal>());
        Assert.Equal("CN-0001", sheet.Cell(headerRow + 1, FindColumn(sheet, headerRow, "Credit Note")).GetString());
        Assert.Equal(0m, sheet.Cell(headerRow + 2, financial).GetValue<decimal>());
        Assert.Equal(118m, sheet.Cell(headerRow + 2, original).GetValue<decimal>());
        Assert.Equal("Void reversal", sheet.Cell(headerRow + 3, 2).GetString());

        Assert.Equal(50m, sheet.Cell(totalsRow, financial).GetValue<decimal>());
        Assert.Equal(7.62m, sheet.Cell(totalsRow, gst).GetValue<decimal>());
        Assert.Equal(-50m, sheet.Cell(totalsRow, refund).GetValue<decimal>());
        Assert.Equal(
            sheet.Cell(totalsRow, financial).GetValue<decimal>(),
            sheet.Cell(totalsRow, receivable).GetValue<decimal>()
                + sheet.Cell(totalsRow, storeCredit).GetValue<decimal>()
                + sheet.Cell(totalsRow, refund).GetValue<decimal>());
    }

    [Fact]
    public async Task RenderAsync_ReturnAdjustmentsSheet_FlagsReturnsNeedingReview()
    {
        var legacy = Adjustment("RET-9", "INV-001", 118m, 0m, 0m) with
        {
            Status = ReturnAccountingStatus.NeedsReview,
            ReviewReason = "Payout settlement is not evidenced.",
            PolicyVersion = null,
            TaxableAdjustment = null,
            TaxAdjustment = null,
            NonTaxAdjustment = null,
        };

        var sheet = await RenderAdjustmentsAsync([legacy]);

        var headerRow = FindRow(sheet, "Posting Date");
        Assert.Equal("Needs review", sheet.Cell(headerRow + 1, FindColumn(sheet, headerRow, "Accounting Status")).GetString());
        Assert.Equal("Payout settlement is not evidenced.", sheet.Cell(headerRow + 1, FindColumn(sheet, headerRow, "Review Reason")).GetString());
        Assert.True(sheet.Cell(headerRow + 1, FindColumn(sheet, headerRow, "GST Adjustment")).IsEmpty());
    }

    [Fact]
    public async Task RenderAsync_SummarySheet_ReconcilesReturnsOnEarlierSales()
    {
        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-002", new DateTimeOffset(2026, 5, 3, 10, 0, 0, TimeSpan.Zero), new DateOnly(2026, 5, 3), "Bob", "Cash", 118m, 0m, 118m, 0m, 100m, 18m, 118m, "RET-2", 118m, 100m, 18m, 0m, true, 1)
        };
        var inPeriod = Adjustment("RET-2", "INV-002", 118m, 100m, 18m,
            new ReturnAdjustmentSettlement(ReturnSettlementKind.ExternalRefund, 118m, null, null, null));
        var earlier = Adjustment("RET-1", "INV-001", 50m, 42.37m, 7.63m,
            new ReturnAdjustmentSettlement(ReturnSettlementKind.StoreCreditIssued, 50m, null, Guid.NewGuid(), "CN-0001"));
        var dataset = new SalesExportDatasetDto(
            Metadata(SalesExportLevel.Summary),
            summaryRows,
            [],
            [new SalesExportTaxBreakupDto(18m, 100m, 18m, 142.37m, 25.63m)],
            [inPeriod, earlier]);

        var result = await new SalesExcelExportRenderer().RenderAsync(dataset, CancellationToken.None);
        using var stream = new MemoryStream(result.Content);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Sales Summary");

        var headerRow = FindRow(sheet, "Invoice #");
        var earlierRow = FindRow(sheet, "Returns on earlier sales");
        var totalsRow = FindRow(sheet, "TOTALS");
        var returnAmount = FindColumn(sheet, headerRow, "Return Amount");
        var netSales = FindColumn(sheet, headerRow, "Net Sales Amount");

        Assert.Equal(headerRow + 2, earlierRow);
        Assert.Equal("RET-1", sheet.Cell(earlierRow, FindColumn(sheet, headerRow, "Return #")).GetString());
        Assert.Equal(50m, sheet.Cell(earlierRow, returnAmount).GetValue<decimal>());
        Assert.Equal(-50m, sheet.Cell(earlierRow, netSales).GetValue<decimal>());
        Assert.Equal(168m, sheet.Cell(totalsRow, returnAmount).GetValue<decimal>());
        Assert.Equal(-50m, sheet.Cell(totalsRow, netSales).GetValue<decimal>());

        var adjustments = workbook.Worksheet("Return Adjustments");
        var adjustmentTotals = FindRow(adjustments, "TOTALS");
        var adjustmentHeader = FindRow(adjustments, "Posting Date");
        Assert.Equal(
            sheet.Cell(totalsRow, returnAmount).GetValue<decimal>(),
            adjustments.Cell(adjustmentTotals, FindColumn(adjustments, adjustmentHeader, "Financial Adjustment")).GetValue<decimal>());
    }

    [Fact]
    public async Task RenderAsync_SummarySheet_TotalsReturnsWhenThePeriodHasOnlyEarlierSales()
    {
        var earlier = Adjustment("RET-1", "INV-001", 50m, 42.37m, 7.63m,
            new ReturnAdjustmentSettlement(ReturnSettlementKind.ExternalRefund, 50m, null, null, null));
        var dataset = new SalesExportDatasetDto(
            Metadata(SalesExportLevel.Summary),
            [],
            [],
            [new SalesExportTaxBreakupDto(18m, 0m, 0m, 42.37m, 7.63m)],
            [earlier]);

        var result = await new SalesExcelExportRenderer().RenderAsync(dataset, CancellationToken.None);
        using var stream = new MemoryStream(result.Content);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Sales Summary");

        var headerRow = FindRow(sheet, "Invoice #");
        var totalsRow = FindRow(sheet, "TOTALS");
        Assert.Equal(50m, sheet.Cell(totalsRow, FindColumn(sheet, headerRow, "Return Amount")).GetValue<decimal>());
        Assert.Equal(-50m, sheet.Cell(totalsRow, FindColumn(sheet, headerRow, "Net Sales Amount")).GetValue<decimal>());
    }

    private static async Task<IXLWorksheet> RenderAdjustmentsAsync(IReadOnlyList<ReturnAdjustmentEntry> adjustments)
    {
        var dataset = new SalesExportDatasetDto(Metadata(SalesExportLevel.LineItems), [], [], [], adjustments);
        var result = await new SalesExcelExportRenderer().RenderAsync(dataset, CancellationToken.None);
        var workbook = new XLWorkbook(new MemoryStream(result.Content));
        Assert.True(workbook.TryGetWorksheet("Sales Line Items", out _));
        return workbook.Worksheet("Return Adjustments");
    }

    private static SalesExportMetadataDto Metadata(string level) =>
        new(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            level);

    private static ReturnAdjustmentEntry Adjustment(
        string returnNumber,
        string invoiceNumber,
        decimal financial,
        decimal taxable,
        decimal tax,
        params ReturnAdjustmentSettlement[] settlements) =>
        new(
            ReturnAdjustmentEntryKind.Return,
            new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 5, 4),
            Guid.NewGuid(),
            returnNumber,
            new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 5, 4),
            Guid.NewGuid(),
            invoiceNumber,
            new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 5, 1),
            "Alice",
            ReturnAccountingStatus.Balanced,
            "proportional-line-tax/1",
            null,
            OriginalReturnedGross: financial < 0m ? -118m : 118m,
            financial,
            taxable,
            tax,
            0m,
            tax == 0m ? [] : [new ReturnAdjustmentTaxComponent(18m, taxable, tax)],
            settlements,
            [],
            null);

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
