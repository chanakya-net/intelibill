namespace Intelibill.Application.Features.Items.Barcodes;

public sealed record BarcodeLabelPrintRow(
    Guid ItemId,
    Guid? InventoryBatchId,
    string ItemName,
    string Barcode,
    string ShopName,
    decimal? Mrp,
    decimal? SalesPrice);
