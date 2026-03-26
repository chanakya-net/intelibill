namespace InventoryAI.Application.Features.Auth.Commands.RegisterWithPhone;

public sealed record RegisterWithPhoneCommand(
    string PhoneNumber,
    string FirstName,
    string LastName);
