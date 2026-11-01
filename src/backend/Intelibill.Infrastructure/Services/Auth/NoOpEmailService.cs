using Intelibill.Application.Common.Interfaces;
namespace Intelibill.Infrastructure.Services.Auth;
internal sealed class NoOpEmailService : IEmailService
{
    public Task SendIdentityAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Email delivery is disabled.");
}
