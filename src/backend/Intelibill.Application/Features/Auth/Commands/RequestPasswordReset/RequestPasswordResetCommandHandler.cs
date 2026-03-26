using System.Security.Cryptography;
using System.Text;
using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

using FluentValidation;
using Intelibill.Application.Common.Extensions;

namespace Intelibill.Application.Features.Auth.Commands.RequestPasswordReset;

public sealed class RequestPasswordResetCommandHandler(
    IValidator<RequestPasswordResetCommand> validator,
    IUserRepository userRepository,
    IPasswordResetTokenRepository passwordResetTokenRepository,
    IEmailService emailService,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<bool>> HandleAsync(
        RequestPasswordResetCommand command,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateCommandAsync(command, cancellationToken);
        if (validationResult is { IsError: true } err) return err.Errors;

        var user = await userRepository.GetByEmailAsync(command.Email, cancellationToken);

        // Always return success to avoid revealing whether the email exists.
        if (user is null || user.PasswordHash is null)
            return true;

        // Invalidate any existing tokens before issuing a new one.
        await passwordResetTokenRepository.InvalidateAllForUserAsync(user.Id, cancellationToken);

        var (rawToken, tokenHash) = GenerateToken();

        var resetToken = PasswordResetToken.Create(user.Id, tokenHash, expiryHours: 2);
        await passwordResetTokenRepository.AddAsync(resetToken, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var resetLink = $"{command.AppBaseUrl.TrimEnd('/')}/reset-password" +
                        $"?token={Uri.EscapeDataString(rawToken)}" +
                        $"&email={Uri.EscapeDataString(command.Email)}";

        await emailService.SendPasswordResetAsync(command.Email, resetLink, cancellationToken);

        return true;
    }

    private static (string RawToken, string TokenHash) GenerateToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        // URL-safe base64
        var rawToken = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        var tokenHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
        return (rawToken, tokenHash);
    }
}
