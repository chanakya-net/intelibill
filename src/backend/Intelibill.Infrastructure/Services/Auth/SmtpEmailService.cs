using System.Globalization;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Infrastructure.Options;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace Intelibill.Infrastructure.Services.Auth;

internal sealed partial class SmtpEmailService(
    IOptions<EmailOptions> options,
    ISmtpClientFactory smtpClientFactory,
    ILogger<SmtpEmailService> logger) : IEmailService
{
    public async Task SendPasswordResetAsync(string toEmail, string resetLink, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        var emailOptions = options.Value;

        await using var smtpClient = smtpClientFactory.Create();
        var message = BuildPasswordResetMessage(emailOptions, toEmail, resetLink, expiresAt);
        var socketOptions = emailOptions.LocalCapture ? SecureSocketOptions.None : emailOptions.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;

        await smtpClient.ConnectAsync(emailOptions.Host, emailOptions.Port, socketOptions, cancellationToken);
        if (!emailOptions.LocalCapture) await smtpClient.AuthenticateAsync(emailOptions.Username, emailOptions.Password, cancellationToken);
        await smtpClient.SendAsync(message, cancellationToken);
        await smtpClient.DisconnectAsync(quit: true, cancellationToken);

        LogPasswordResetEmailSent(logger, toEmail);
    }

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
        await client.ConnectAsync(settings.Host, settings.Port, settings.LocalCapture ? SecureSocketOptions.None : settings.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto, cancellationToken);
        if (!settings.LocalCapture) await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    internal static MimeMessage BuildPasswordResetMessage(
        EmailOptions emailOptions,
        string toEmail,
        string resetLink,
        DateTimeOffset expiresAt)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(emailOptions.FromName, emailOptions.FromEmail));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = "Reset your Intelibill password";
        message.Body = new TextPart(TextFormat.Plain)
        {
            Text = BuildPasswordResetBody(resetLink, expiresAt)
        };

        return message;
    }

    internal static string BuildPasswordResetBody(string resetLink, DateTimeOffset expiresAt)
    {
        var expiryTime = ConvertToIst(expiresAt).ToString("hh:mm tt", CultureInfo.InvariantCulture);

        return
            $"Here is your password reset link:{Environment.NewLine}{Environment.NewLine}" +
            $"{resetLink}{Environment.NewLine}{Environment.NewLine}" +
            $"This link expires at {expiryTime} IST and can be used only once.{Environment.NewLine}{Environment.NewLine}" +
            "If you did not request this, you can ignore this email.";
    }

    private static DateTimeOffset ConvertToIst(DateTimeOffset utcTime)
    {
        foreach (var timeZoneId in new[] { "India Standard Time", "Asia/Kolkata" })
        {
            try
            {
                var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return TimeZoneInfo.ConvertTime(utcTime, timeZone);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return utcTime.ToOffset(TimeSpan.FromHours(5.5));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "[SMTPEmail] Password reset email sent to {Email}")]
    private static partial void LogPasswordResetEmailSent(ILogger logger, string email);
}
