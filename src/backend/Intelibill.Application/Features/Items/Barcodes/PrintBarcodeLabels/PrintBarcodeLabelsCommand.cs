using Intelibill.Application.Features.Items.Barcodes;

namespace Intelibill.Application.Features.Items.Barcodes.PrintBarcodeLabels;

public sealed record PrintBarcodeLabelsCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    IReadOnlyList<PrintBarcodeLabelItemRequest> Items);
