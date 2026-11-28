using System.Globalization;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Licensing.Infrastructure.Status;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Infrastructure.Notifications;

internal sealed record EmailLease(Guid Id, Guid AccountId, Guid Token);
internal sealed record EmailClaim(bool Found, EmailLease? Lease);

internal sealed class NotificationEmailDeliveryStore(
    LicensingDbContext db,
    [FromKeyedServices(SubscriptionStatusRegistration.ClockKey)] TimeProvider businessClock,
    [FromKeyedServices(BillingNotificationWorker.LeaseClock)] TimeProvider leaseClock)
{
    private sealed record DueRow(Guid Id, Guid AccountId, Guid? Token);

    internal async Task<EmailClaim> ClaimAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = leaseClock.GetUtcNow();
        await EnterAsync(null, now, ct);
        var rows = await db.Database.SqlQuery<DueRow>($"""
            SELECT * FROM public.next_notice_email({businessClock.GetUtcNow()}, {now})
            """).ToListAsync(ct);
        if (rows is not [var due])
            return new(false, null);

        await tx.CommitAsync(ct);
        return new(true, due.Token is { } token ? new(due.Id, due.AccountId, token) : null);
    }

    internal Task<int> EnterAsync(Guid? account, DateTimeOffset now, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('app.notification_planner','on',true), set_config('app.notice_email_worker','on',true),
                set_config('app.billing_account_id',{account?.ToString("D") ?? ""},true),
                set_config('app.notice_email_clock',{now.ToString("O", CultureInfo.InvariantCulture)},true)
            """, ct);

    internal Task<bool> BeginSendAsync(EmailLease lease, CancellationToken ct) =>
        AdvanceAsync(lease, "Leased", "Sending", leaseClock.GetUtcNow(), ct);

    internal async Task FinishAsync(EmailLease lease, string expected, string outcome, CancellationToken ct)
    {
        var now = leaseClock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await EnterAsync(lease.AccountId, now, ct);
        await AdvanceAsync(lease, expected, outcome, now, ct);
        await tx.CommitAsync(ct);
    }

    private Task<bool> AdvanceAsync(EmailLease lease, string from, string to, DateTimeOffset now, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT public.advance_notice_email({lease.AccountId}, {lease.Id}, {lease.Token}, {from}, {to}, {now}) AS "Value"
            """).SingleAsync(ct);
}
