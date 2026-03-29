namespace Intelibill.Application.Features.Users.DTOs;

public sealed record ShopUserDto(
    Guid UserId,
    string FirstName,
    string LastName,
    string? Email,
    string? PhoneNumber,
    string Role);
