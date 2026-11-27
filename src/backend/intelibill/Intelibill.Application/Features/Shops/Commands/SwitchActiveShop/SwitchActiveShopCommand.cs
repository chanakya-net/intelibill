namespace Intelibill.Application.Features.Shops.Commands.SwitchActiveShop;

/// <summary>
/// <paramref name="SessionId"/> is the device making the request. The scope change applies to that
/// session alone, so switching shops on the shop counter does not move the owner's phone.
/// </summary>
public sealed record SwitchActiveShopCommand(Guid UserId, Guid SessionId, Guid ShopId);
