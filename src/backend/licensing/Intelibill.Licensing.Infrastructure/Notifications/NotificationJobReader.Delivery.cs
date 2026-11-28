using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Infrastructure.AdminIdentities;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Intelibill.Licensing.Infrastructure.Notifications;

internal sealed class BillingEmailCaptureLinks(IOptions<BillingEmailOptions> options, IOptions<AdminMailOptions> smtp, IHostEnvironment env)
{
    internal string? For(Guid id, string state)
    {
        if (state is not ("SmtpAccepted" or "Uncertain") || !options.Value.CanCapture(smtp.Value, env)
            || BillingEmailOptions.LocalOrigin(options.Value.MailpitUiOrigin) is not { } origin)
            return null;
        return new Uri(origin, "/search?q=" + Uri.EscapeDataString($"message-id:{id:N}@notices.intelibill.invalid")).AbsoluteUri;
    }
}

internal sealed partial class NotificationJobReader
{
    private sealed record EmailDetail(Guid Id, Guid JobId, string State, int Attempts,
        DateTimeOffset? LastAttemptAt, DateTimeOffset? NextAttemptAt, DateTimeOffset? OutcomeAt, string? Reason);

    private async Task<NotificationReadPage> WithDeliveryAsync(Guid account, NotificationReadPage page, CancellationToken ct)
    {
        var ids = page.Rows.Select(row => row.Id).ToArray();
        var deliveries = await db.Database.SqlQuery<EmailDetail>($"""
            SELECT d.id, d.job_id, d.state, d.attempts, d.last_attempt_at,
                CASE WHEN d.state = 'RetryPending' THEN d.next_attempt_at END AS next_attempt_at, d.outcome_at, e.reason
            FROM public.notification_email_deliveries d
            LEFT JOIN public.notification_email_events e ON d.state = 'Suppressed' AND e.state = d.state
                AND e.account_id = d.account_id AND e.delivery_id = d.id AND e.attempt = d.attempts
            WHERE d.account_id = {account} AND d.job_id = ANY({ids})
            """).ToDictionaryAsync(row => row.JobId, ct);
        return page with
        {
            Rows = page.Rows.Select(job => deliveries.TryGetValue(job.Id, out var d)
                ? job with { EmailDelivery = new ProviderEmailDeliveryV1(d.State, d.Attempts, d.LastAttemptAt,
                    d.NextAttemptAt, d.OutcomeAt, links.For(d.Id, d.State),
                    LicenseNoticesV1.SuppressionReasons.Contains(d.Reason) ? d.Reason : null) }
                : job).ToArray(),
        };
    }
}
