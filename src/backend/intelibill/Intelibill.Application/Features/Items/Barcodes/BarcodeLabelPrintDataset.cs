namespace Intelibill.Application.Features.Items.Barcodes;

public sealed record BarcodeLabelPrintDataset(
    IReadOnlyList<BarcodeLabelPrintRow> Rows);
