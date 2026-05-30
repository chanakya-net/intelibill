using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Auth.Commands.ExternalLogin;

public sealed record ExternalLoginCommand(
    ExternalAuthProvider Provider,
    string Token,
    // Apple only sends the name on the very first sign-in via the front-end, not in the JWT.
    // Pass it along so we can capture it on account creation.
    string? FirstName = null,
    string? LastName = null);
