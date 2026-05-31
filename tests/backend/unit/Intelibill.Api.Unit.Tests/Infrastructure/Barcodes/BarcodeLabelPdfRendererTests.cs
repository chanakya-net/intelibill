using Intelibill.Application.Features.Items.Barcodes;
using Intelibill.Infrastructure.Services.Barcodes;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Barcodes;

public sealed class BarcodeLabelPdfRendererTests
{
    [Fact]
    public async Task RenderAsync_WithSingleLabelRow_ReturnsPdfBytes()
    {
        var renderer = new BarcodeLabelPdfRenderer();
        var dataset = new BarcodeLabelPrintDataset(
        [
            new BarcodeLabelPrintRow(Guid.NewGuid(), null, "Toor Dal", "IB-000001", "Green Mart", 120m, 110m),
        ]);

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);

        Assert.Equal("application/pdf", result.ContentType);
        Assert.NotEmpty(result.Content);
        Assert.True(result.Content.AsSpan(0, Math.Min(result.Content.Length, 5)).SequenceEqual("%PDF-"u8));
    }

    [Fact]
    public async Task RenderAsync_WithRepeatedRows_ReturnsLargerPdf()
    {
        var renderer = new BarcodeLabelPdfRenderer();
        var row = new BarcodeLabelPrintRow(Guid.NewGuid(), null, "Rice", "IB-000002", "Green Mart", 80m, 75m);

        var singleResult = await renderer.RenderAsync(new BarcodeLabelPrintDataset([row]), CancellationToken.None);
        var repeatedResult = await renderer.RenderAsync(new BarcodeLabelPrintDataset([row, row, row]), CancellationToken.None);

        Assert.Equal("application/pdf", repeatedResult.ContentType);
        Assert.True(repeatedResult.Content.Length > singleResult.Content.Length);
    }
}
