namespace Intelibill.Application.Features.Items.Barcodes;

public sealed record BarcodeLabelPrintResult(
    byte[] Content,
    string ContentType,
    string FileName);
