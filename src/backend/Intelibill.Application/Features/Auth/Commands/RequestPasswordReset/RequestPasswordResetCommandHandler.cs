using System.Security.Cryptography;
using System.Text;
using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Auth.Commands.RequestPasswordReset;

/// <summary>
/// Issues a reset proof and queues its link in one transaction. Delivery happens in the identity
/// mail worker, so a provider outage after commit delays the message instead of losing the only
/// copy of the link, and a rollback leaves neither a proof nor a message behind.
/// </summary>
public sealed class RequestPasswordResetCommandHandler(
    IUserRepository userRepository,
    IPasswordResetTokenRepository passwordResetTokenRepository,
    IIdentityMailOutbox mail,
    IEmailOwnershipRepository ownership)
{
    public async Task<ErrorOr<bool>> HandleAsync(
        RequestPasswordResetCommand command,
        CancellationToken cancellationToken)
    {
        // Checked before any lookup, so the 503 says nothing about whether the account exists.
        if (!mail.Enabled)
            return Error.Failure("Auth.EmailDeliveryUnavailable", "Email delivery is temporarily unavailable.");
        var user = await userRepository.GetByEmailAsync(command.Email, cancellationToken);

        // Always return success to avoid revealing whether the email exists.
        if (user is null || !user.IsEmailVerified || !user.IsLoginEnabled || user.PasswordHash is null)
            return true;

        await using (var transaction = await ownership.BeginAsync(cancellationToken))
        {
            // The initial lookup is only a snapshot: recheck under the same locks as
            // email changes before minting authority for its destination/version.
            if (!await ownership.LockCurrentCredentialAsync(user.Id, user.SecurityVersion, user.Email!, cancellationToken)) return true;
            // Invalidate any existing tokens before issuing a new one; their queued mail is
            // dropped by the worker once it sees them used.
            await passwordResetTokenRepository.InvalidateAllForUserAsync(user.Id, cancellationToken);

            var (rawToken, tokenHash) = GenerateToken();

            var resetToken = PasswordResetToken.Create(user.Id, tokenHash, expiryMinutes: 15, securityVersion: user.SecurityVersion, destination: user.Email!);
            await passwordResetTokenRepository.AddAsync(resetToken, cancellationToken);
            mail.QueuePasswordReset(resetToken.Id, resetToken.Destination, rawToken, resetToken.ExpiresAt);
            await ownership.CommitAsync(cancellationToken);
        }

        // Generic whatever happened above, including a lost race: the response never reveals
        // whether an account exists or whether a message was queued.
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
