namespace Intelibill.Application.Features.Items.Queries.GetProductDetails;

public sealed record GetProductDetailsByNameOrBarcodeQuery(
    Guid UserId,
    Guid ActiveShopId,
    string? ProductName,
    string? Barcode);
