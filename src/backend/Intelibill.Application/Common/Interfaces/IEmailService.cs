namespace Intelibill.Application.Common.Interfaces;

public interface IEmailService
{
    Task SendPasswordResetAsync(string toEmail, string resetLink, CancellationToken cancellationToken = default);
}
