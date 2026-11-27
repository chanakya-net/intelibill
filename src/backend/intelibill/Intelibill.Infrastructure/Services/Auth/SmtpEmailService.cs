using Intelibill.Application.Common.Interfaces;
using Intelibill.Infrastructure.Options;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace Intelibill.Infrastructure.Services.Auth;

internal sealed class SmtpEmailService(
    IOptions<EmailOptions> options,
    ISmtpClientFactory smtpClientFactory) : IEmailService
{
    public async Task SendIdentityAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.Enabled) throw new InvalidOperationException("Email delivery is disabled.");
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromEmail));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart(TextFormat.Plain) { Text = body };
        await using var client = smtpClientFactory.Create();
        await client.ConnectAsync(settings.Host, settings.Port, SocketOptionsFor(settings), cancellationToken);
        if (!settings.LocalCapture) await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    /// <summary>
    /// Every message carries a bearer link or a security notice, so a real transport is always
    /// encrypted: STARTTLS when configured, implicit TLS otherwise. <see cref="SecureSocketOptions.Auto"/>
    /// is never used because it continues in plaintext when the server offers no TLS.
    /// </summary>
    internal static SecureSocketOptions SocketOptionsFor(EmailOptions settings) =>
        settings.LocalCapture ? SecureSocketOptions.None
        : settings.UseStartTls ? SecureSocketOptions.StartTls
        : SecureSocketOptions.SslOnConnect;
}
