namespace Intelibill.Application.Features.Users.Commands.EditShopUser;

public sealed record EditShopUserCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid TargetUserId,
    string FirstName,
    string LastName,
    string PhoneNumber,
    string Role,
    bool IsLoginEnabled);
