using Intelibill.Application.Features.Exports.Accounting;
using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Application.Features.Exports.Sales.DTOs;
using Intelibill.Domain.Enums;
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
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Cash", 200m, 50m, 250m, 0m, 250m, 50m, 300m, null, 0m, 0m, 0m, 300m, false, 1, 50m, "CN-001", 500m)
        };

        var baselineRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Cash", 200m, 50m, 250m, 0m, 250m, 50m, 300m, null, 0m, 0m, 0m, 300m, false, 1)
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

        Assert.True(ContainsStreamDifferences(creditResult.Content, baselineResult.Content));
    }

    [Fact]
    public async Task RenderAsync_WithReturnAdjustments_IncludesReturnAdjustmentSection()
    {
        var metadata = Metadata();
        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Cash", 118m, 0m, 118m, 0m, 100m, 18m, 118m, "RET-001", 100m, 84.75m, 15.25m, 18m, true, 1, 0m, "CN-001", 60m)
        };

        var baselineDataset = new SalesExportDatasetDto(metadata, summaryRows, [], [], []);
        var dataset = new SalesExportDatasetDto(metadata, summaryRows, [], [], [MixedReturn("INV-001")]);
        var renderer = new SalesPdfExportRenderer();

        var baselineResult = await renderer.RenderAsync(baselineDataset, CancellationToken.None);
        var result = await renderer.RenderAsync(dataset, CancellationToken.None);

        Assert.Equal("application/pdf", result.ContentType);
        Assert.NotEmpty(result.Content);
        Assert.True(ContainsStreamDifferences(result.Content, baselineResult.Content));
    }

    [Fact]
    public void SummarizeReturns_CountsEveryAdjustmentPostedInPeriod_IncludingEarlierSales()
    {
        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-002", new DateTimeOffset(2026, 5, 3, 10, 0, 0, TimeSpan.Zero), "Bob", "Cash", 236m, 0m, 236m, 0m, 200m, 36m, 236m, null, 0m, 0m, 0m, 236m, false, 1)
        };
        var earlierSaleReturn = MixedReturn("INV-001");
        var needsReview = MixedReturn("INV-000") with
        {
            ReturnNumber = "RET-LEGACY",
            Status = ReturnAccountingStatus.NeedsReview,
            FinancialAdjustment = 10m,
            Settlements = [],
        };

        var totals = SalesPdfDocument.SummarizeReturns(
            new SalesExportDatasetDto(Metadata(), summaryRows, [], [], [earlierSaleReturn, needsReview]));

        Assert.Equal(110m, totals.Adjustment);
        Assert.Equal(60m, totals.StoreCreditIssued);
        Assert.Equal(126m, totals.NetSales);
        Assert.Equal(1, totals.NeedingReview);
    }

    private static SalesExportMetadataDto Metadata() =>
        new(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            SalesExportLevel.Summary);

    private static ReturnAdjustmentEntry MixedReturn(string invoiceNumber) =>
        new(
            ReturnAdjustmentEntryKind.Return,
            new DateTimeOffset(2026, 5, 2, 11, 0, 0, TimeSpan.Zero),
            Guid.NewGuid(),
            "RET-001",
            new DateTimeOffset(2026, 5, 2, 11, 0, 0, TimeSpan.Zero),
            Guid.NewGuid(),
            invoiceNumber,
            "Alice",
            ReturnAccountingStatus.Balanced,
            "proportional-line-tax/1",
            null,
            OriginalReturnedGross: 118m,
            FinancialAdjustment: 100m,
            TaxableAdjustment: 84.75m,
            TaxAdjustment: 15.25m,
            NonTaxAdjustment: 0m,
            [new ReturnAdjustmentTaxComponent(18m, 84.75m, 15.25m)],
            [
                new ReturnAdjustmentSettlement(ReturnSettlementKind.ReceivableReduction, 40m, null, null, null),
                new ReturnAdjustmentSettlement(ReturnSettlementKind.StoreCreditIssued, 60m, null, Guid.NewGuid(), "CN-001"),
            ],
            [],
            null);

    private static bool ContainsStreamDifferences(byte[] creditResult, byte[] baselineResult) =>
        !string.Equals(
            Convert.ToBase64String(creditResult),
            Convert.ToBase64String(baselineResult),
            StringComparison.Ordinal);
}
