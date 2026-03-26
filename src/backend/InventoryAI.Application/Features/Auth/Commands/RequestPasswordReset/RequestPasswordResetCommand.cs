namespace InventoryAI.Application.Features.Auth.Commands.RequestPasswordReset;

public sealed record RequestPasswordResetCommand(string Email, string AppBaseUrl);
