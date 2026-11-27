namespace Intelibill.Application.Features.Auth.Commands.RequestPasswordReset;

/// <remarks>
/// Carries no link origin: the outbox builds reset links from server configuration only, so no
/// caller can steer a recovery link to a host of its choosing.
/// </remarks>
public sealed record RequestPasswordResetCommand(string Email);
