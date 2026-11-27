namespace Intelibill.Application.Features.Auth.Commands.RefreshToken;

/// <summary>
/// Rotates a refresh token. <paramref name="PreferredShopId"/> is the shop the expiring access
/// token was scoped to; it is a preference only, honoured when the caller still holds a live
/// membership there and ignored otherwise, so it grants nothing by itself.
/// </summary>
/// <remarks>The string form leaves the secret out, so logging the message cannot leak it.</remarks>
public sealed record RefreshTokenCommand(string RefreshToken, Guid? PreferredShopId = null)
{
    public override string ToString() => $"{nameof(RefreshTokenCommand)} {{ PreferredShopId = {PreferredShopId} }}";
}
