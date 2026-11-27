using Intelibill.Application.Common.Security;
namespace Intelibill.Application.Features.Items.Barcodes.GenerateItemBarcode;

public sealed record GenerateItemBarcodeCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    SessionClaims? Session);
