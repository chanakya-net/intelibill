namespace Intelibill.Application.Features.Shops.Commands.SetDefaultShop;

/// <summary>
/// Saves which shop opens on the next sign-in. <paramref name="ActiveShopId"/> is the scope the
/// requesting device is already working in and is echoed back unchanged: a stored preference must
/// not move a device out of the shop its operator is standing in. <paramref name="SecurityVersion"/>
/// is the account version the caller's token names, so a preference cannot be written by a session
/// the account has already invalidated.
/// </summary>
public sealed record SetDefaultShopCommand(Guid UserId, Guid ShopId, Guid? ActiveShopId, long SecurityVersion = -1);
