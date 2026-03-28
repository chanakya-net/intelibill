namespace Intelibill.Application.Features.Users.Commands.UpdateMyProfile;

public sealed record UpdateMyProfileCommand(
    Guid UserId,
    string Email,
    string? PhoneNumber,
    string FirstName,
    string LastName);
