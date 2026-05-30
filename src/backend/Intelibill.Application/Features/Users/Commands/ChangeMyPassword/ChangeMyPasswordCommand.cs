namespace Intelibill.Application.Features.Users.Commands.ChangeMyPassword;

public sealed record ChangeMyPasswordCommand(
    Guid UserId,
    string CurrentPassword,
    string NewPassword);
