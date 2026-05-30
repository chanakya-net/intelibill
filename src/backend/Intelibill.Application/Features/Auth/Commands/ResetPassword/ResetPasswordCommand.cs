namespace Intelibill.Application.Features.Auth.Commands.ResetPassword;

public sealed record ResetPasswordCommand(string Email, string Token, string NewPassword);
