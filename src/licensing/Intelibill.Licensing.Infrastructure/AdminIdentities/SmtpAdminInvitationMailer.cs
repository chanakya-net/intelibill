using System.Globalization;
using System.Net.Sockets;
using Intelibill.Licensing.Application.AdminIdentities;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace Intelibill.Licensing.Infrastructure.AdminIdentities;

internal sealed partial class SmtpAdminInvitationMailer(
    IOptions<AdminMailOptions> options, IHostEnvironment environment, ILogger<SmtpAdminInvitationMailer> logger)
    : IAdminInvitationMailer
{
    public async Task<bool> TrySendAsync(AdminInvitationMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;
        var local = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        if (!settings.IsUsable(local))
        {
            LogUnconfigured(logger);
            return false;
        }

        try
        {
            await SendAsync(settings, Compose(settings, message), cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is IOException or SocketException or TimeoutException
            or ProtocolException or CommandException or SslHandshakeException or AuthenticationException)
        {
            // Only the type: transport messages can echo addresses or server responses.
            LogFailed(logger, exception.GetType().Name);
            return false;
        }
    }

    private static async Task SendAsync(AdminMailOptions settings, MimeMessage message, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient { Timeout = 15_000 };
        // A real transport always carries this bearer link encrypted; Auto could fall back to plaintext.
        var security = settings.LocalCapture ? SecureSocketOptions.None
            : settings.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;
        await client.ConnectAsync(settings.Host, settings.Port, security, cancellationToken);
        if (!settings.LocalCapture) await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    private static MimeMessage Compose(AdminMailOptions settings, AdminInvitationMessage invitation)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromEmail));
        message.To.Add(MailboxAddress.Parse(invitation.Recipient));
        message.Subject = "Your InteliBill Provider Console invitation";
        var expiry = invitation.ExpiresAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        message.Body = new TextPart(TextFormat.Plain)
        {
            Text = $"""
                You have been invited to administer the InteliBill Provider Console.

                Accept the invitation:
                {invitation.Link.AbsoluteUri}

                This one-use link expires at {expiry}. You will confirm this email address and choose a
                password, then set up an authenticator app before you can use the console. The link
                works only for this email address.

                If you did not expect this invitation, ignore this message.
                """,
        };
        return message;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Admin invitation delivery is not configured for this environment.")]
    private static partial void LogUnconfigured(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Admin invitation delivery failed ({ExceptionType}).")]
    private static partial void LogFailed(ILogger logger, string exceptionType);
}
