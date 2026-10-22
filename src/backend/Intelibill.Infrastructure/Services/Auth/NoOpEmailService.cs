using Intelibill.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
namespace Intelibill.Infrastructure.Services.Auth;
internal sealed class NoOpEmailService : IEmailService
{
    public NoOpEmailService(ILogger<NoOpEmailService> logger) { ArgumentNullException.ThrowIfNull(logger); }
    public Task SendPasswordResetAsync(string toEmail, string resetLink, DateTimeOffset expiresAt, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Email delivery is disabled.");
}
