using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Application.Features.Exports.Sales.DTOs;
using Intelibill.Infrastructure.Services.Exports;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Exports;

public sealed class SalesPdfExportRendererTests
{
    [Fact]
    public async Task RenderAsync_WithSummaryDataset_ReturnsNonEmptyPdfBytes()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            SalesExportLevel.Summary);

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Cash", 1200m, 0m, 1200m, 0m, 1000m, 180m, 1180m, null, 0m, 0m, 0m, 1180m, false, 2)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, [], [], []);
        var renderer = new SalesPdfExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);

        Assert.Equal("application/pdf", result.ContentType);
        Assert.NotEmpty(result.Content);
        Assert.True(result.Content.AsSpan(0, Math.Min(result.Content.Length, 5)).SequenceEqual("%PDF-"u8));
    }

    [Fact]
    public async Task RenderAsync_WithLineItemDataset_ReturnsNonEmptyPdfBytes()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            SalesExportLevel.LineItems);

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Cash", 1200m, 0m, 1200m, 0m, 1000m, 180m, 1180m, null, 0m, 0m, 0m, 1180m, false, 2)
        };

        var lineItemRows = new List<SalesExportLineItemRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Apple", 2m, 50m, 0m, 0m, 18m, 100m, 18m, 118m, false, 0m, null, null)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, lineItemRows, [], []);
        var renderer = new SalesPdfExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);

        Assert.Equal("application/pdf", result.ContentType);
        Assert.NotEmpty(result.Content);
        Assert.True(result.Content.AsSpan(0, Math.Min(result.Content.Length, 5)).SequenceEqual("%PDF-"u8));
    }

    [Fact]
    public async Task RenderAsync_WithCreditNoteFields_IncludesCreditNoteMetricsInSummary()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            SalesExportLevel.Summary);

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Cash", 1200m, 0m, 1200m, 0m, 1000m, 180m, 1180m, null, 0m, 0m, 0m, 1180m, false, 2, 200m, "CN-001", 500m)
        };

        var baselineRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Cash", 1200m, 0m, 1200m, 0m, 1000m, 180m, 1180m, null, 0m, 0m, 0m, 1180m, false, 2)
        };

        var creditDataset = new SalesExportDatasetDto(metadata, summaryRows, [], [], []);
        var baselineDataset = new SalesExportDatasetDto(metadata, baselineRows, [], [], []);
        var renderer = new SalesPdfExportRenderer();

        var creditResult = await renderer.RenderAsync(creditDataset, CancellationToken.None);
        var baselineResult = await renderer.RenderAsync(baselineDataset, CancellationToken.None);

        Assert.Equal("application/pdf", creditResult.ContentType);
        Assert.NotEmpty(creditResult.Content);
        Assert.Equal("application/pdf", baselineResult.ContentType);
        Assert.NotEmpty(baselineResult.Content);
        Assert.NotEqual(baselineResult.Content.Length, creditResult.Content.Length);

        // Credit-note summary values only appear in report content bytes when added
        // to invoice rows, so the output should differ from a baseline dataset.
        Assert.NotEqual(baselineResult.Content.Length, creditResult.Content.Length);
    }
}
