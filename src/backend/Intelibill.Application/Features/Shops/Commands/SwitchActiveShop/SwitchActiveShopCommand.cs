namespace Intelibill.Application.Features.Shops.Commands.SwitchActiveShop;

public sealed record SwitchActiveShopCommand(Guid UserId, Guid ShopId, long SecurityVersion = -1);
