using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Licensing.Infrastructure.Restrictions;
using Intelibill.Licensing.Infrastructure.Status;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Infrastructure.Notifications;

internal sealed class ReminderSourceReader(
    LicensingDbContext db,
    SubscriptionAccessFactsReader facts,
    [FromKeyedServices(SubscriptionStatusRegistration.ClockKey)] TimeProvider clock)
{
    internal DateTimeOffset Now => SubscriptionStatusProjection.EvaluationInstant(clock);

    internal async Task<NoticeRelevance> ReadAsync(ReminderEmail job, CancellationToken ct)
    {
        var now = Now;
        if (await ClosureStore.AccountClosedAsync(db, job.AccountId, ct))
            return new("ScopeClosed");
        var supersededAt = await SupersededAtAsync(job, ct);
        if (supersededAt <= now)
            return new("SourceNoLongerCurrent");
        var source = (await NotificationPlanSourceQueries.ReadTermsByIdAsync(db, facts, job.AccountId, [job.SourceId], now, ct))
            .SingleOrDefault();
        if (source is null)
            return new("SourceNoLongerCurrent");
        var shops = source.Shops.Where(s => !s.IsClosed).ToArray();
        var suspended = await NotificationPlanSourceQueries.IsAccountSuspendedAsync(db, job.AccountId, now, ct);

        var queued = new QueuedNoticeThreshold(job.SourceKind, job.SourceRevision, job.Purpose, job.Threshold, job.DueAt, job.Deadline);
        NoticeRelevance result;
        Guid[] shopIds;
        if (job.AffiliationRequestId.HasValue)
        {
            result = await ReadShopAsync(job, queued, source, suspended, now, ct);
            shopIds = [job.ScopeId];
        }
        else
        {
            if (shops.Length == 0 && source.Shops.Count > 0)
                return new("ScopeClosed");
            var restriction = NotificationPlanner.ResolveRestrictionScope(suspended, shops, out _);
            result = ObsoleteNoticePolicy.Evaluate(queued, source, null, restriction, now);
            shopIds = shops.Select(shop => shop.ShopId).ToArray();
        }
        if (result.Current is null)
            return result;
        var nextRestriction = await db.Database.SqlQuery<DateTimeOffset?>($"""
            SELECT min(effective_at) AS "Value" FROM public.active_operational_restrictions
            WHERE account_id = {job.AccountId} AND (shop_id IS NULL OR shop_id = ANY({shopIds})) AND effective_at > {now}
            """).SingleAsync(ct);
        return result.Until(supersededAt).Until(nextRestriction);
    }

    private async Task<NoticeRelevance> ReadShopAsync(
        ReminderEmail job,
        QueuedNoticeThreshold queued,
        TermPlanSource source,
        bool suspended,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var shop = source.Shops.SingleOrDefault(s => s.ShopId == job.ScopeId && s.AffiliationRequestId == job.AffiliationRequestId);
        if (shop is null)
            return new("SourceNoLongerCurrent");
        if (shop.IsClosed)
            return new("ScopeClosed");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.status_shop_id',{job.ScopeId.ToString("D")},true)", ct);
        var coverage = await facts.ReadShopAsync(job.ScopeId, ct, now);
        // The lease transition requires worker scope with no shop or actor GUC.
        await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.status_shop_id','',true)", ct);
        if (coverage?.AccountId != job.AccountId || coverage.Term?.TermId != job.SourceId)
            return new("SourceNoLongerCurrent");
        var baseline = SubscriptionStatusProjection.ToFacts(coverage.Term);
        var grants = (coverage.Grants ?? []).Select(SubscriptionStatusProjection.ToFacts).ToArray();
        if (ComplimentaryAccessPolicy.Select(baseline, grants, now) != -1)
            return new("SourceNoLongerCurrent");
        var restriction = suspended || shop.IsSuspended ? NoticeRestrictionScope.Full : NoticeRestrictionScope.None;
        var nextGrant = grants.Where(grant => grant.StartsAt > now).Min(grant => (DateTimeOffset?)grant.StartsAt);
        return ObsoleteNoticePolicy.Evaluate(queued, source, shop, restriction, now).Until(nextGrant);
    }

    private Task<DateTimeOffset?> SupersededAtAsync(ReminderEmail job, CancellationToken ct) =>
        db.Database.SqlQuery<DateTimeOffset?>($"""
            SELECT min(changed_at) AS "Value" FROM (
                SELECT fulfilled_at AS changed_at FROM public.payment_fulfilments
                WHERE account_id = {job.AccountId} AND predecessor_term_id = {job.SourceId}
                UNION ALL
                SELECT paid_starts_at FROM public.payment_trial_conversions
                WHERE account_id = {job.AccountId} AND trial_term_id = {job.SourceId}
            ) changes
            """).SingleAsync(ct);
}
