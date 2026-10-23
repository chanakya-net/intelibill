namespace Intelibill.Application.Features.Auth.Commands.RefreshToken;

/// <summary>
/// Rotates a refresh token. <paramref name="PreferredShopId"/> is the shop the expiring access
/// token was scoped to; it is a preference only, honoured when the caller still holds a live
/// membership there and ignored otherwise, so it grants nothing by itself.
/// </summary>
public sealed record RefreshTokenCommand(string RefreshToken, Guid? PreferredShopId = null);
