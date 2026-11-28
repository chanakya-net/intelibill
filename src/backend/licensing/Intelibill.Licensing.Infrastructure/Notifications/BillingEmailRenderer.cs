using System.Globalization;
using Intelibill.Licensing.Domain.Policies;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using MimeKit;
using MimeKit.Text;

namespace Intelibill.Licensing.Infrastructure.Notifications;

internal static class BillingEmailRenderer
{
    internal static readonly Dictionary<string, JsonElement> Templates =
        LicenseNoticesV1.SupportedLocales.ToDictionary(locale => locale, LoadTemplate, StringComparer.Ordinal);

    private static JsonElement LoadTemplate(string locale)
    {
        var assembly = typeof(BillingEmailRenderer).Assembly;
        var name = $"{assembly.GetName().Name}.Notifications.Templates.{locale}.json";
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("Missing billing email locale.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    internal static MimeMessage? Reminder(
        BillingEmailOptions options, EmailLease lease, ReminderEmail job, string email, string audience,
        PlannedThreshold current, DateTimeOffset now)
    {
        var content = Templates[LicenseNoticesV1.NormalizeLocale(job.Locale)];
        var guidanceKey = audience == LicenseNoticesV1.AudienceShopOwnerOperational ? "operationalGuidance" : "guidance";
        if (!MatchesTemplate(job) || !content.GetProperty("purposes").TryGetProperty(job.Purpose, out var subject)
            || !content.GetProperty("thresholds").TryGetProperty(job.Threshold, out var threshold)
            || !content.GetProperty(guidanceKey).TryGetProperty(current.GuidanceCode, out var guidance))
            return null;
        var culture = CultureInfo.GetCultureInfo(LicenseNoticesV1.NormalizeLocale(job.Locale));
        var local = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(job.Deadline, "Asia/Kolkata");
        var deadline = $"{local.ToString("F", culture)} IST ({job.Deadline:O} UTC)";
        var body = $"{guidance.GetString()}\n{content.GetProperty("effectiveDeadline").GetString()}: {deadline}";
        if (now == current.DueAt)
            body = $"{threshold.GetString()}\n{body}";
        if (current.PaymentDoesNotLiftSuspension && current.GuidanceCode is "PaidTermCancelled" or "AccessExpired")
            body += $"\n{content.GetProperty("suspensionNoLift").GetString()}";
        if (audience == LicenseNoticesV1.AudienceCommercialBillingRole)
        {
            if (job is { PricePaise: >= 0, Currency: "INR" })
            {
                var price = (job.PricePaise.Value / 100m).ToString("N2", culture);
                var label = content.GetProperty("evidencedTermPrice").GetString();
                var quote = content.GetProperty("renewalQuoteNote").GetString();
                body += $"\n{label}: INR {price}\n{quote}";
            }
            body += $"\n{new Uri(new Uri(options.MerchantOrigin), $"/billing/accounts/{job.AccountId:D}/status")}";
        }
        return Compose(options, lease.Id, email, subject.GetString()!, body);
    }

    private static bool MatchesTemplate(ReminderEmail job) => (job.SourceKind, job.Purpose, job.Threshold, job.TemplateKey) switch
    {
        ("Trial", "TrialReminder", "TrialMinus3Days", "notice.trial.minus3" or "notice.suspended.trial.minus3") => true,
        ("Trial", "TrialReminder", "TrialMinus1Day", "notice.trial.minus1" or "notice.suspended.trial.minus1") => true,
        ("Paid", "PaidRenewalReminder", "PaidMinus7Days", "notice.paid.minus7" or "notice.paid.cancelled.minus7") => true,
        ("Paid", "PaidRenewalReminder", "PaidMinus1Day", "notice.paid.minus1" or "notice.paid.cancelled.minus1") => true,
        ("Paid", "GraceNotice", "GraceStart", "notice.grace.start") => true,
        ("Paid", "GraceNotice", "GraceMinus1Day", "notice.grace.minus1") => true,
        ("Paid", "GraceNotice", "GraceExpiry", "notice.grace.expiry" or "notice.suspended.grace.expiry") => true,
        _ => false,
    };

    internal static (string Subject, string Body) ProofText(string locale)
    {
        var proof = Templates[LicenseNoticesV1.NormalizeLocale(locale)].GetProperty("proof");
        return (proof.GetProperty("subject").GetString()!, proof.GetProperty("body").GetString()!);
    }

    internal static MimeMessage Compose(BillingEmailOptions options, Guid id, string address, string subject, string body)
    {
        var message = new MimeMessage
        {
            MessageId = $"{id:N}@notices.intelibill.invalid",
            Subject = subject,
            Body = new TextPart(TextFormat.Plain) { Text = body },
        };
        message.From.Add(new MailboxAddress("InteliBill", options.FromEmail));
        message.To.Add(new MailboxAddress("", address));
        message.ReplyTo.Add(new MailboxAddress("", options.SupportEmail));
        return message;
    }
}
