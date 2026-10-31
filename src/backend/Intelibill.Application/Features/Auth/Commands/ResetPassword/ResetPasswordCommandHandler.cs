using System.Security.Cryptography;
using System.Text;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Auth.Commands.ResetPassword;

public sealed class ResetPasswordCommandHandler(
    IUserRepository userRepository,
    IPasswordResetTokenRepository passwordResetTokenRepository,
    IAuthSessionRepository sessions,
    IPasswordHasher passwordHasher,
    IEmailOwnershipRepository ownership,
    TimeProvider clock)
{
    public async Task<ErrorOr<bool>> HandleAsync(
        ResetPasswordCommand command,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByEmailAsync(command.Email, cancellationToken);
        if (user is null || !user.IsEmailVerified || !user.IsLoginEnabled)
            return Errors.Auth.InvalidPasswordResetToken;

        await using var transaction = await ownership.BeginAsync(cancellationToken);
        if (!await ownership.LockCurrentCredentialAsync(user.Id, user.SecurityVersion, user.Email!, cancellationToken))
            return Errors.Auth.InvalidPasswordResetToken;
        var resetToken = await passwordResetTokenRepository.GetValidByUserIdAsync(user.Id, cancellationToken);
        if (resetToken is null || !resetToken.IsValid || resetToken.SecurityVersion != user.SecurityVersion ||
            !string.Equals(resetToken.Destination, user.Email, StringComparison.OrdinalIgnoreCase))
            return Errors.Auth.InvalidPasswordResetToken;

        var incomingHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(command.Token)));
        if (!string.Equals(incomingHash, resetToken.TokenHash, StringComparison.Ordinal))
            return Errors.Auth.InvalidPasswordResetToken;

        user.UpdatePassword(passwordHasher.Hash(command.NewPassword));
        userRepository.Update(user);

        resetToken.MarkAsUsed();
        passwordResetTokenRepository.Update(resetToken);

        // Every device is ended, with its refresh family, inside the same transaction that writes
        // the new password. Whoever is recovering the account does not know which devices are
        // signed in, so none is spared.
        await sessions.RevokeAllAsync(user.Id, exceptSessionId: null, "password-reset", clock.GetUtcNow(), cancellationToken);

        if (!await ownership.CommitAsync(cancellationToken)) return Errors.Auth.InvalidPasswordResetToken;

        return true;
    }
}
