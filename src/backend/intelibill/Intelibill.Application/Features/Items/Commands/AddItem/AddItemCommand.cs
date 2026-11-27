using Intelibill.Application.Common.Security;

namespace Intelibill.Application.Features.Items.Commands.AddItem;

public sealed record AddItemCommand(
    SessionClaims Session,
    string Name,
    string Barcode,
    string? Description,
    string Uom,
    bool IsActive,
    string? HsnCode,
    decimal DefaultTaxRatePercent,
    int? QuantityScale = null)
{
    public Guid ActorUserId => Session.UserId;
    public Guid ActiveShopId => Session.ActiveShopId.GetValueOrDefault();
}
