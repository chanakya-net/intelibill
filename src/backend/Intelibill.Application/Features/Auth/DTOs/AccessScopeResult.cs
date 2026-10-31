namespace Intelibill.Application.Features.Auth.DTOs;

/// <summary>
/// What a scope change returns: a replacement access token for the device that asked, and no new
/// refresh secret. Choosing a shop is not a credential event, so it must not create one.
/// </summary>
public sealed record AccessScopeResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    Guid? ActiveShopId,
    long ScopeVersion,
    IReadOnlyList<UserShopDto> Shops);

/// <summary>
/// What saving the default shop returns: the stored preference, with no token at all. The device
/// that saved it keeps the scope it already had.
/// </summary>
public sealed record ShopPreferenceResult(
    Guid? ActiveShopId,
    IReadOnlyList<UserShopDto> Shops);
