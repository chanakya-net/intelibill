using Intelibill.Application.Common.Security;
namespace Intelibill.Application.Features.Items.Commands.UpdateItem;

public sealed record UpdateItemCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid ItemId,
    string Name,
    string Barcode,
    string? Description,
    string Uom,
    string? HsnCode,
    decimal DefaultTaxRatePercent,
    SessionClaims? Session,
    bool? IsActive = null,
    int? QuantityScale = null);
