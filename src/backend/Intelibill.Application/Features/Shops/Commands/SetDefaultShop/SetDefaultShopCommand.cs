namespace Intelibill.Application.Features.Shops.Commands.SetDefaultShop;

/// <summary>
/// Saves which shop opens on the next sign-in. <paramref name="ActiveShopId"/> is the scope the
/// requesting device is already working in and is echoed back unchanged: a stored preference must
/// not move a device out of the shop its operator is standing in.
/// </summary>
public sealed record SetDefaultShopCommand(Guid UserId, Guid ShopId, Guid? ActiveShopId);
