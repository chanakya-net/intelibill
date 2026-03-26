using System.Security.Cryptography;
using System.Text;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

using FluentValidation;
using Intelibill.Application.Common.Extensions;

namespace Intelibill.Application.Features.Auth.Commands.ResetPassword;

public sealed class ResetPasswordCommandHandler(
    IValidator<ResetPasswordCommand> validator,
    IUserRepository userRepository,
    IPasswordResetTokenRepository passwordResetTokenRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<bool>> HandleAsync(
        ResetPasswordCommand command,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateCommandAsync(command, cancellationToken);
        if (validationResult is { IsError: true } err) return err.Errors;

        var user = await userRepository.GetByEmailAsync(command.Email, cancellationToken);
        if (user is null)
            return Errors.Auth.InvalidPasswordResetToken;

        var resetToken = await passwordResetTokenRepository.GetValidByUserIdAsync(user.Id, cancellationToken);
        if (resetToken is null)
            return Errors.Auth.InvalidPasswordResetToken;

        var incomingHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(command.Token)));
        if (!string.Equals(incomingHash, resetToken.TokenHash, StringComparison.Ordinal))
            return Errors.Auth.InvalidPasswordResetToken;

        user.UpdatePassword(passwordHasher.Hash(command.NewPassword));
        userRepository.Update(user);

        resetToken.MarkAsUsed();
        passwordResetTokenRepository.Update(resetToken);

        // Revoke all active refresh tokens so existing sessions cannot be reused.
        await refreshTokenRepository.RevokeAllForUserAsync(user.Id, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
