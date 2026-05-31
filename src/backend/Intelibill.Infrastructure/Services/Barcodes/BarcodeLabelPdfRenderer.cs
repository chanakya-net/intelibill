using Intelibill.Application.Features.Items.Barcodes;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Intelibill.Infrastructure.Services.Barcodes;

public sealed class BarcodeLabelPdfRenderer : IBarcodeLabelPdfRenderer
{
    private const string ContentType = "application/pdf";
    private const string FileName = "barcode-labels.pdf";

    static BarcodeLabelPdfRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public Task<BarcodeLabelPrintResult> RenderAsync(
        BarcodeLabelPrintDataset dataset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pdfContent = new BarcodeLabelPdfDocument(dataset).GeneratePdf();
        return Task.FromResult(new BarcodeLabelPrintResult(pdfContent, ContentType, FileName));
    }
}
