using Intelibill.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Intelibill.Infrastructure.Services.Auth;

internal sealed partial class NoOpEmailService(ILogger<NoOpEmailService> logger) : IEmailService
{
    public Task SendPasswordResetAsync(string toEmail, string resetLink, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        LogPasswordResetLink(logger, toEmail, resetLink, expiresAt);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "[NoOpEmail] Password reset link for {Email}: {ResetLink} (expires at {ExpiresAt})")]
    private static partial void LogPasswordResetLink(ILogger logger, string email, string resetLink, DateTimeOffset expiresAt);
}
