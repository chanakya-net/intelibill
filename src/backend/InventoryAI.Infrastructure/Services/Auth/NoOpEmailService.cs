using InventoryAI.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace InventoryAI.Infrastructure.Services.Auth;

internal sealed partial class NoOpEmailService(ILogger<NoOpEmailService> logger) : IEmailService
{
    public Task SendPasswordResetAsync(string toEmail, string resetLink, CancellationToken cancellationToken = default)
    {
        LogPasswordResetLink(logger, toEmail, resetLink);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "[NoOpEmail] Password reset link for {Email}: {ResetLink}")]
    private static partial void LogPasswordResetLink(ILogger logger, string email, string resetLink);
}
