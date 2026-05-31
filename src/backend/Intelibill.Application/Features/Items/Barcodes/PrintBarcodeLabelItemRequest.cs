namespace Intelibill.Application.Features.Items.Barcodes;

public sealed record PrintBarcodeLabelItemRequest(
    Guid ItemId,
    int Quantity,
    Guid? InventoryBatchId);

public sealed record PrintBarcodeLabelsRequest(
    IReadOnlyList<PrintBarcodeLabelItemRequest> Items);
