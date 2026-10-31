using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Auth.Commands.LoginWithEmail;

public sealed class LoginWithEmailCommandHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IAuthSessionService sessions,
    TimeProvider clock)
{
    public async Task<ErrorOr<AuthResult>> HandleAsync(
        LoginWithEmailCommand command,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByEmailAsync(command.Email, cancellationToken);

        if (user is null || user.PasswordHash is null || !passwordHasher.Verify(command.Password, user.PasswordHash))
            return Errors.Auth.InvalidCredentials;

        if (!user.IsLoginEnabled)
            return Errors.Auth.UserLoginDisabled;

        if (user.Email is not null && !user.IsEmailVerified)
            return Error.Conflict("Auth.EmailVerificationRequired", "Use mailbox recovery to verify your email before signing in.");

        return await sessions.StartAsync(
            AuthenticationProof.FromPassword(user.Id, user.SecurityVersion, clock.GetUtcNow()),
            preferredShopId: null,
            cancellationToken);
    }
}
