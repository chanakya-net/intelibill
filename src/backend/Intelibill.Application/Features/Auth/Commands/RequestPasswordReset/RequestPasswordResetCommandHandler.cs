using System.Security.Cryptography;
using System.Text;
using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Entities;
using Microsoft.Extensions.Logging;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Auth.Commands.RequestPasswordReset;

public sealed partial class RequestPasswordResetCommandHandler(
    IUserRepository userRepository,
    IPasswordResetTokenRepository passwordResetTokenRepository,
    IEmailService emailService,
    IEmailOwnershipRepository ownership,
    ILogger<RequestPasswordResetCommandHandler> logger)
{
    public async Task<ErrorOr<bool>> HandleAsync(
        RequestPasswordResetCommand command,
        CancellationToken cancellationToken)
    {
        if (!emailService.IsAvailable)
            return Error.Failure("Auth.EmailDeliveryUnavailable", "Email delivery is temporarily unavailable.");
        var user = await userRepository.GetByEmailAsync(command.Email, cancellationToken);

        // Always return success to avoid revealing whether the email exists.
        if (user is null || !user.IsEmailVerified || !user.IsLoginEnabled || user.PasswordHash is null)
            return true;

        PasswordResetToken resetToken;
        string rawToken;
        await using (var transaction = await ownership.BeginAsync(cancellationToken))
        {
            // The initial lookup is only a snapshot: recheck under the same locks as
            // email changes before minting authority for its destination/version.
            if (!await ownership.LockCurrentCredentialAsync(user.Id, user.SecurityVersion, user.Email!, cancellationToken)) return true;
            // Invalidate any existing tokens before issuing a new one.
            await passwordResetTokenRepository.InvalidateAllForUserAsync(user.Id, cancellationToken);

            (rawToken, var tokenHash) = GenerateToken();

            resetToken = PasswordResetToken.Create(user.Id, tokenHash, expiryMinutes: 15, securityVersion: user.SecurityVersion, destination: user.Email!);
            await passwordResetTokenRepository.AddAsync(resetToken, cancellationToken);
            if (!await ownership.CommitAsync(cancellationToken)) return true;
        }

        var resetLink = $"{command.AppBaseUrl.TrimEnd('/')}/reset-password" +
                        $"?token={Uri.EscapeDataString(rawToken)}" +
                        $"&email={Uri.EscapeDataString(resetToken.Destination)}";

        try { await emailService.SendPasswordResetAsync(resetToken.Destination, resetLink, resetToken.ExpiresAt, cancellationToken); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Do not expose account-specific delivery failures, addresses, or bearer URLs.
            LogDeliveryFailed(logger);
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Password reset delivery failed; request remains generic.")]
    private static partial void LogDeliveryFailed(ILogger logger);

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
