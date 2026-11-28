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
        DateTimeOffset? LastAttemptAt, DateTimeOffset? NextAttemptAt, DateTimeOffset? OutcomeAt);

    private async Task<NotificationReadPage> WithDeliveryAsync(Guid account, NotificationReadPage page, CancellationToken ct)
    {
        var ids = page.Rows.Select(row => row.Id).ToArray();
        var deliveries = await db.Database.SqlQuery<EmailDetail>($"""
            SELECT id, job_id, state, attempts, last_attempt_at,
                CASE WHEN state = 'RetryPending' THEN next_attempt_at END AS next_attempt_at, outcome_at
            FROM public.notification_email_deliveries WHERE account_id = {account} AND job_id = ANY({ids})
            """).ToDictionaryAsync(row => row.JobId, ct);
        return page with
        {
            Rows = page.Rows.Select(job => deliveries.TryGetValue(job.Id, out var d)
                ? job with { EmailDelivery = new ProviderEmailDeliveryV1(d.State, d.Attempts, d.LastAttemptAt,
                    d.NextAttemptAt, d.OutcomeAt, links.For(d.Id, d.State)) }
                : job).ToArray(),
        };
    }
}
