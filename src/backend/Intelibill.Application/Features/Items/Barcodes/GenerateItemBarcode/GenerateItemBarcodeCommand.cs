namespace Intelibill.Application.Features.Items.Barcodes.GenerateItemBarcode;

public sealed record GenerateItemBarcodeCommand(
    Guid ActorUserId,
    Guid ActiveShopId);
