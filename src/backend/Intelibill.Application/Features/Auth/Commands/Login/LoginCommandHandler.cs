using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Auth.Commands.Login;

public sealed class LoginCommandHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IAuthSessionService sessions,
    TimeProvider clock)
{
    public async Task<ErrorOr<AuthResult>> HandleAsync(
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        var trimmedIdentifier = command.Identifier.Trim();
        var isEmailIdentifier = trimmedIdentifier.Contains('@');
        var user = isEmailIdentifier
            ? await userRepository.GetByEmailAsync(trimmedIdentifier.ToLowerInvariant(), cancellationToken)
            : await userRepository.GetByPhoneAsync(trimmedIdentifier, cancellationToken);

        if (user is null || user.PasswordHash is null || !passwordHasher.Verify(command.Password, user.PasswordHash))
            return Errors.Auth.InvalidCredentials;

        if (!user.IsLoginEnabled)
            return Errors.Auth.UserLoginDisabled;

        if (user.Email is not null && !user.IsEmailVerified)
            return Error.Conflict("Auth.EmailVerificationRequired", "Use mailbox recovery to verify your email before signing in.");

        // The proof names the account version the password was actually checked against. Password
        // verification is deliberately slow, so a reset or a disablement can commit while it runs;
        // session creation re-reads the account under its lock and refuses a proof that has been
        // overtaken, rather than stamping it with whatever the version is by then.
        return await sessions.StartAsync(
            AuthenticationProof.FromPassword(user.Id, user.SecurityVersion, clock.GetUtcNow()),
            preferredShopId: null,
            cancellationToken);
    }
}
