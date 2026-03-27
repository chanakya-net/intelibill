namespace Intelibill.Application.Features.Shops.Commands.SetDefaultShop;

public sealed record SetDefaultShopCommand(Guid UserId, Guid ShopId);