using System.Net.Sockets;
using Intelibill.Licensing.Infrastructure.AdminIdentities;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Intelibill.Licensing.Infrastructure.Notifications;

internal sealed class BillingEmailTransport(
    IOptions<BillingEmailOptions> email,
    IOptions<AdminMailOptions> transport,
    IHostEnvironment environment)
{
    internal bool Enabled => email.Value.CanCapture(transport.Value, environment);

    internal async Task<bool> ConnectAsync(SmtpClient client, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await client.ConnectAsync(transport.Value.Host, transport.Value.Port, SecureSocketOptions.None, budget.Token);
            return true;
        }
        catch (Exception error) when (IsTransportFailure(error))
        {
            ct.ThrowIfCancellationRequested();
            return false;
        }
    }

    internal static async Task<string> SubmitAsync(SmtpClient client, MimeMessage message, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await client.SendAsync(message, budget.Token);
            return "SmtpAccepted";
        }
        catch (SmtpCommandException rejection) when (rejection.ErrorCode is SmtpErrorCode.MessageNotAccepted
            or SmtpErrorCode.SenderNotAccepted or SmtpErrorCode.RecipientNotAccepted)
        {
            return (int)rejection.StatusCode is >= 400 and < 500 ? "RetryPending" : "Failed";
        }
        catch (Exception error) when (IsTransportFailure(error))
        {
            return "Uncertain";
        }
    }

    private static bool IsTransportFailure(Exception error) =>
        error is IOException or SocketException or TimeoutException or ProtocolException or CommandException or OperationCanceledException;
}
