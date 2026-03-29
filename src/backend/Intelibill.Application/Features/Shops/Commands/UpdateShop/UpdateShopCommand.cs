namespace Intelibill.Application.Features.Shops.Commands.UpdateShop;

public sealed record UpdateShopCommand(
    Guid UserId,
    Guid ShopId,
    string Name,
    string Address,
    string City,
    string State,
    string Pincode,
    string? ContactPerson,
    string? MobileNumber,
    string? GstNumber);
