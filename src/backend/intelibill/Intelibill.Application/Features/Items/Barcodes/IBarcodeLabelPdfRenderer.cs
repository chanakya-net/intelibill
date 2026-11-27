namespace Intelibill.Application.Features.Items.Barcodes;

public interface IBarcodeLabelPdfRenderer
{
    Task<BarcodeLabelPrintResult> RenderAsync(
        BarcodeLabelPrintDataset dataset,
        CancellationToken cancellationToken);
}
