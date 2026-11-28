using System.Net;
using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Infrastructure.AdminIdentities;
using Microsoft.Extensions.Hosting;
using MimeKit;

namespace Intelibill.Licensing.Infrastructure.Notifications;

internal sealed class BillingEmailOptions
{
    public bool Enabled { get; init; }
    public bool RunInBackground { get; init; } = true;
    public string FromEmail { get; init; } = "";
    public string SupportEmail { get; init; } = "";
    public string MerchantOrigin { get; init; } = "";
    public string VerificationOrigin { get; init; } = "";
    public string MailpitUiOrigin { get; init; } = "";

    internal bool CanCapture(AdminMailOptions smtp, IHostEnvironment env) =>
        Enabled && (env.IsDevelopment() || env.IsEnvironment("Testing"))
        && smtp.LocalCapture && smtp.Port is > 0 and <= 65535 && IsCaptureHost(smtp.Host)
        && smtp.Username.Length == 0 && smtp.Password.Length == 0
        && SyntheticMailbox(FromEmail) && SyntheticMailbox(SupportEmail)
        && LocalOrigin(MerchantOrigin) is not null && LocalOrigin(VerificationOrigin) is not null;

    private static bool SyntheticMailbox(string value) =>
        Mailbox(value) && (value.EndsWith(".test", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase));

    internal static bool Mailbox(string value) =>
        value is { Length: > 0 and <= 254 } && !value.Any(char.IsControl) && value == value.Trim()
        && BillingContactConsentHandler.IsValidEmail(value)
        && MailboxAddress.TryParse(value, out var parsed) && parsed.Address == value && parsed.Name is null or "";

    internal static Uri? LocalOrigin(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Host is "localhost" or "127.0.0.1" or "[::1]"
        && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0
        && uri.PathAndQuery == "/" && uri.Fragment.Length == 0 ? uri : null;

    private static bool IsCaptureHost(string host) => host is "mailpit" or "localhost"
        || (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
}
