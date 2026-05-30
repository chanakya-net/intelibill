using ClosedXML.Excel;
using Intelibill.Application.Features.Exports.ProfitLoss;
using Intelibill.Application.Features.Exports.ProfitLoss.DTOs;
using Intelibill.Infrastructure.Services.Exports;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Exports;

public sealed class ProfitLossExcelExportRendererTests
{
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
