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
    Guid? SessionId = null);

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
