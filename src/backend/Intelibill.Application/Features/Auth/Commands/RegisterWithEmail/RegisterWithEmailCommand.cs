namespace Intelibill.Application.Features.Auth.Commands.RegisterWithEmail;

public sealed record RegisterWithEmailCommand(
    string Email,
    string Password,
    string FirstName,
    string LastName);
