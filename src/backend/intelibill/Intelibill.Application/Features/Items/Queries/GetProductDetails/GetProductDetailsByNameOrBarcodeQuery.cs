using Intelibill.Application.Common.Security;

namespace Intelibill.Application.Features.Items.Queries.GetProductDetails;

public sealed record GetProductDetailsByNameOrBarcodeQuery(
    SessionClaims Session,
    string? ProductName,
    string? Barcode,
    string? AuthorizationHeader)
{
    public Guid UserId => Session.UserId;
    public Guid ActiveShopId => Session.ActiveShopId.GetValueOrDefault();
}
