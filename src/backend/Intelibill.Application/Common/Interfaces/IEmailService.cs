namespace Intelibill.Application.Common.Interfaces;

public interface IEmailService
{
    Task SendIdentityAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Identity email delivery is unavailable.");
    Task SendPasswordResetAsync(string toEmail, string resetLink, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);
}
