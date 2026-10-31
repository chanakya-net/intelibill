namespace Intelibill.Application.Features.Auth.DTOs;

public sealed record AuthResult(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt,
    UserDto User,
    Guid? ActiveShopId = null,
    IReadOnlyList<UserShopDto>? Shops = null,
    /// <summary>The device session this result belongs to, so a client can recognise itself in its own session list.</summary>
    Guid? SessionId = null)
{
    /// <summary>Leaves both bearer credentials out, so logging a result cannot leak them.</summary>
    public override string ToString() =>
        $"{nameof(AuthResult)} {{ UserId = {User.Id}, SessionId = {SessionId}, ActiveShopId = {ActiveShopId}, AccessTokenExpiresAt = {AccessTokenExpiresAt:O}, RefreshTokenExpiresAt = {RefreshTokenExpiresAt:O} }}";
}

public sealed record UserDto(
    Guid Id,
    string? Email,
    string? PhoneNumber,
    string FirstName,
    string LastName,
    string Language = "en-IN");

public sealed record UserShopDto(
    Guid ShopId,
    string ShopName,
    string Role,
    bool IsDefault,
    DateTimeOffset? LastUsedAt);
