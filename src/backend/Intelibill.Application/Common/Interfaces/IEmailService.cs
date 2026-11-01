namespace Intelibill.Application.Common.Interfaces;

/// <summary>
/// Transport for identity mail. Callers never send directly: messages are composed and encrypted
/// by <see cref="IIdentityMailOutbox"/> inside the transaction that creates their proof, and the
/// delivery worker is the only caller of this seam.
/// </summary>
public interface IEmailService
{
    Task SendIdentityAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default);
}
