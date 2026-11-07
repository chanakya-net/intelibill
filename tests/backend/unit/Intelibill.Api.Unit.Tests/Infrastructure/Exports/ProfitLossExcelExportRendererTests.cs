using ClosedXML.Excel;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Exports.ProfitLoss;
using Intelibill.Application.Features.Exports.ProfitLoss.DTOs;
using Intelibill.Infrastructure.Services.Exports;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Exports;

public sealed class ProfitLossExcelExportRendererTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenderAsync_UsesVersionedEconomicsAndPreservesUnavailableValues(bool unavailable)
    {
        var economics = new ProfitLossEconomicsDto(2, "recorded-sale-cost/1", 200m, 100m, 0m, 0m, 100m,
            20m, unavailable ? null : 80m, 50m, 100m, unavailable ? 1 : 0, unavailable ? 20m : 0m,
            unavailable ? "UnclassifiedExpenses" : "Complete", ["purchase-tax-recoverability-unmodeled"]);
        var dataset = new ProfitLossExportDatasetDto(new("Shop", null, "Owner", DateTimeOffset.UtcNow, new(2026, 6, 1), new(2026, 6, 30), "all", null, "xlsx"),
            new(100m, 200m, 100m, 100m, 1, 0, 0, economics),
            [new("=Literal", DateTimeOffset.UtcNow, "Sale", "Name", 100m, 0m, 200m, 200m, 100m, 100m, 100m,
                new(2, 200m, 100m, 0m, 0m, 100m, 50m, 100m, false))]);
        var result = await new ProfitLossExcelExportRenderer().RenderAsync(dataset, CancellationToken.None);
        using var book = new XLWorkbook(new MemoryStream(result.Content));
        var sheet = book.Worksheet("Profit & Loss");
        decimal Value(string label) => sheet.RowsUsed().Single(r => r.Cell(1).GetString() == label).Cell(2).GetValue<decimal>();
        Assert.Equal(200m, Value("Net sales excluding tax"));
        Assert.Equal(100m, Value("COGS"));
        Assert.Equal(100m, Value("Gross trading profit"));
        Assert.Equal(20m, Value("Operating expenses"));
        var operating = sheet.RowsUsed().Single(r => r.Cell(1).GetString() == "Operating profit").Cell(2);
        if (unavailable) Assert.Equal("Not available", operating.GetString());
        else Assert.Equal(80m, operating.GetValue<decimal>());
        Assert.Equal(50m, Value("Gross margin %"));
        Assert.Equal(100m, Value("Markup %"));
        Assert.DoesNotContain(sheet.CellsUsed(), c => c.GetString() == "Net Profit After Tax");
        Assert.False(sheet.CellsUsed().Single(c => c.GetString() == "=Literal").HasFormula);
    }

    [Fact]
    public async Task RenderAsync_CreatesWorkbookWithExpectedColumnsAndFilename()
    {
        var metadata = new ProfitLossExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "sale",
            "customer",
            "xlsx");

        var summary = new ProfitLossExportSummaryDto(120m, 220m, 100m, 12.5m, 2, 0, 0);
        var rows = new List<ProfitLossExportRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Sale", "Alice", 80m, 0m, 100m, 118m, 20m, 38m, 47.5m),
        };
        var dataset = new ProfitLossExportDatasetDto(metadata, summary, rows);

        var renderer = new ProfitLossExcelExportRenderer();
        var result = await renderer.RenderAsync(dataset, CancellationToken.None);

        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", result.ContentType);
        Assert.Contains("green-mart-profit-loss-2026-05-01-to-2026-05-31.xlsx", result.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(result.Content);

        using var workbook = new XLWorkbook(new MemoryStream(result.Content));
        var sheet = workbook.Worksheet("Profit & Loss");
        var headerRow = sheet.RowsUsed().First(row => row.Cell(1).GetString() == "Reference").RowNumber();

        Assert.Equal("Shop:", sheet.Cell(1, 1).GetString());
        Assert.Equal("Reference", sheet.Cell(headerRow, 1).GetString());
        Assert.Equal("INV-001", sheet.Cell(headerRow + 1, 1).GetString());
        Assert.Equal("Alice", sheet.Cell(headerRow + 1, 4).GetString());
        Assert.Equal(118m, sheet.Cell(headerRow + 1, 8).GetValue<decimal>());
    }
}
