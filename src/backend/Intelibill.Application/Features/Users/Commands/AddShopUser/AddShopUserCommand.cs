namespace Intelibill.Application.Features.Users.Commands.AddShopUser;

public sealed record AddShopUserCommand(
    Guid ActorUserId,
    Guid ShopId,
    string FirstName,
    string LastName,
    string PhoneNumber,
    string Password,
    string ConfirmPassword,
    string Role);
