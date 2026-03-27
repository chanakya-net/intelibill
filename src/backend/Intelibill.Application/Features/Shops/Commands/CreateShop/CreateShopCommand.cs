namespace Intelibill.Application.Features.Shops.Commands.CreateShop;

public sealed record CreateShopCommand(Guid UserId, string Name);