namespace InventoryAI.Application.Features.Auth.DTOs;

public sealed record AuthResult(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt,
    UserDto User);

public sealed record UserDto(
    Guid Id,
    string? Email,
    string? PhoneNumber,
    string FirstName,
    string LastName);
