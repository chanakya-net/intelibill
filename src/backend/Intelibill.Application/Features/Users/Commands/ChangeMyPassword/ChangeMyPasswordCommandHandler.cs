using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Users.Commands.ChangeMyPassword;

/// <summary>
/// Changing a password is a security transition, so it commits as one: the account row is locked,
/// the current password is checked against what that lock guarantees is the live credential, the
/// new password is written and every device is ended — all or nothing.
/// </summary>
/// <remarks>
/// Every device includes the one that asked. Sharing one code path with recovery leaves no second,
/// gentler way to change a password, and the cost is a single sign-in the person is already at the
/// keyboard for.
/// </remarks>
public sealed class ChangeMyPasswordCommandHandler(
    IUserRepository userRepository,
    IAuthSessionRepository sessions,
    IPasswordHasher passwordHasher,
    TimeProvider clock)
{
    public async Task<ErrorOr<bool>> HandleAsync(ChangeMyPasswordCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await sessions.BeginAsync(cancellationToken);

        var authority = await sessions.LockUserAuthorityAsync(command.UserId, cancellationToken);
        if (authority is null)
            return Errors.Auth.UserNotFound;
        if (!authority.CanHoldSession)
            return Errors.Auth.SessionInvalidated;

        var user = await userRepository.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
            return Errors.Auth.UserNotFound;

        // Guards against a user instance loaded before the lock being mistaken for the current one:
        // the password verified below has to be the one the lock is holding.
        if (user.SecurityVersion != authority.SecurityVersion)
            return Errors.Auth.SessionInvalidated;

        if (string.IsNullOrWhiteSpace(user.PasswordHash))
            return Errors.Auth.PasswordNotSet;

        if (!passwordHasher.Verify(command.CurrentPassword, user.PasswordHash))
            return Errors.Auth.InvalidCurrentPassword;

        user.UpdatePassword(passwordHasher.Hash(command.NewPassword));
        userRepository.Update(user);

        await sessions.RevokeAllAsync(
            user.Id,
            exceptSessionId: null,
            "password-changed",
            clock.GetUtcNow(),
            cancellationToken);

        return await sessions.CommitAsync(cancellationToken)
            ? true
            : Errors.Auth.SessionInvalidated;
    }
}
