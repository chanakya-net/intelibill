using ErrorOr;
using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Licensing.Infrastructure.Status;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Notifications;

internal sealed class NotificationJobReader(
    LicensingDbContext db,
    SubscriptionAccessFactsReader factsReader) : ILicenseNoticeReader
{
    public Task<int> ReadTrialExtensionRevisionAsync(Guid accountId, Guid termId, CancellationToken ct) =>
        NotificationPlanSourceQueries.ReadTrialExtensionRevisionAsync(db, accountId, termId, ct);

    public async Task<IReadOnlyList<CurrentCommercialSource>> ReadCurrentAccountSourcesAsync(
        Guid accountId,
        Guid actorId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var isAccountSuspended = await NotificationPlanSourceQueries.IsAccountSuspendedAsync(db, accountId, now, ct);
        var terms = await NotificationPlanSourceQueries.ReadEffectiveTermSourcesAsync(
            db,
            factsReader,
            accountId,
            now,
            termPageSize: 100,
            filterTermIds: null,
            ct,
            displayActor: actorId);
        var sources = new List<CurrentCommercialSource>(terms.Count);
        foreach (var termSource in terms)
        {
            if (BuildCurrentCommercialSource(isAccountSuspended, termSource) is { } current)
                sources.Add(current);
        }

        return sources;
    }

    public async Task<NotificationReadPage> ReadCurrentDueShopNoticeAsync(
        Guid accountId,
        Guid shopId,
        Guid actorId,
        Guid sourceId,
        string sourceRevision,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // Select only the latest due InApp notice for this shop's current effective term revision.
        var rows = await db.Database.SqlQuery<JobRow>($"""
            SELECT id, account_id, scope_kind, scope_id, recipient_key, recipient_user_id, contact_id,
                audience, purpose, source_kind, source_id, source_revision, threshold,
                due_at, effective_deadline_at, time_zone, locale, allowed_channels,
                status, template_key, template_arguments::text AS template_arguments,
                destination_hint, planned_at
            FROM public.effective_notification_jobs
            WHERE account_id = {accountId}
              AND scope_kind = {LicenseNoticesV1.ScopeShop}
              AND scope_id = {shopId}
              AND recipient_user_id = {actorId}
              AND {LicenseNoticesV1.ChannelInApp} = ANY(allowed_channels)
              AND source_id = {sourceId}
              AND source_revision = {sourceRevision}
              AND due_at <= {now}
              AND (effective_deadline_at > {now} OR threshold = {LicenseNoticesV1.ThresholdGraceExpiry})
            ORDER BY due_at DESC, id DESC
            LIMIT 1
            """).ToListAsync(ct);

        var nextDue = await db.Database.SqlQuery<DateTimeOffset?>($"""
            SELECT MIN(CASE WHEN due_at > {now} THEN due_at ELSE effective_deadline_at END) AS "Value"
            FROM public.effective_notification_jobs
            WHERE account_id = {accountId}
              AND scope_kind = {LicenseNoticesV1.ScopeShop}
              AND scope_id = {shopId}
              AND recipient_user_id = {actorId}
              AND {LicenseNoticesV1.ChannelInApp} = ANY(allowed_channels)
              AND source_id = {sourceId}
              AND source_revision = {sourceRevision}
              AND effective_deadline_at > {now}
            """).SingleAsync(ct);

        return ToPage(rows, take: 1, nextDue);
    }

    public async Task<NotificationReadPage> ReadCurrentDueAccountNoticesAsync(
        Guid accountId,
        Guid actorId,
        IReadOnlyList<CurrentCommercialSource> activeSources,
        DateTimeOffset now,
        Guid? after,
        int take,
        CancellationToken ct)
    {
        if (activeSources.Count == 0)
            return new NotificationReadPage([], null, false);

        var sourceIds = activeSources.Select(item => item.SourceId).ToArray();
        var revisions = activeSources.Select(item => item.SourceRevision).ToArray();
        var rows = await db.Database.SqlQuery<JobRow>($"""
            WITH active_sources AS (
                SELECT s.source_id, s.source_revision
                FROM unnest({sourceIds}, {revisions}) AS s(source_id, source_revision)
            ),
            latest_per_source AS (
                SELECT DISTINCT ON (j.source_id)
                    j.id, j.account_id, j.scope_kind, j.scope_id, j.recipient_key, j.recipient_user_id, j.contact_id,
                    j.audience, j.purpose, j.source_kind, j.source_id, j.source_revision, j.threshold,
                    j.due_at, j.effective_deadline_at, j.time_zone, j.locale, j.allowed_channels,
                    j.status, j.template_key, j.template_arguments::text AS template_arguments,
                    j.destination_hint, j.planned_at
                FROM public.effective_notification_jobs j
                JOIN active_sources a
                    ON a.source_id = j.source_id AND a.source_revision = j.source_revision
                WHERE j.account_id = {accountId}
                  AND j.scope_kind = {LicenseNoticesV1.ScopeAccount}
                  AND j.scope_id = {accountId}
                  AND j.recipient_user_id = {actorId}
                  AND j.audience = {LicenseNoticesV1.AudienceCommercialBillingRole}
                  AND {LicenseNoticesV1.ChannelInApp} = ANY(j.allowed_channels)
                  AND j.due_at <= {now}
                  AND (j.effective_deadline_at > {now} OR j.threshold = {LicenseNoticesV1.ThresholdGraceExpiry})
                ORDER BY j.source_id, j.due_at DESC, j.id DESC
            )
            SELECT *
            FROM latest_per_source
            ORDER BY due_at ASC, id ASC
            """).ToListAsync(ct);

        var pagedRows = SliceAfterCursor(rows, after, take, out var hasMore);
        var nextDue = await ReadNextDueAccountNoticeAsync(accountId, actorId, sourceIds, revisions, now, ct);
        return new NotificationReadPage(
            pagedRows.Select(MapRow).ToArray(),
            nextDue?.ToUniversalTime(),
            hasMore);
    }

    private Task<DateTimeOffset?> ReadNextDueAccountNoticeAsync(
        Guid accountId,
        Guid actorId,
        Guid[] sourceIds,
        string[] revisions,
        DateTimeOffset now,
        CancellationToken ct) =>
        db.Database.SqlQuery<DateTimeOffset?>($"""
            WITH active_sources AS (
                SELECT s.source_id, s.source_revision
                FROM unnest({sourceIds}, {revisions}) AS s(source_id, source_revision)
            )
            SELECT MIN(CASE WHEN j.due_at > {now} THEN j.due_at ELSE j.effective_deadline_at END) AS "Value"
            FROM public.effective_notification_jobs j
            JOIN active_sources a
                ON a.source_id = j.source_id AND a.source_revision = j.source_revision
            WHERE j.account_id = {accountId}
              AND j.scope_kind = {LicenseNoticesV1.ScopeAccount}
              AND j.scope_id = {accountId}
              AND j.recipient_user_id = {actorId}
              AND j.audience = {LicenseNoticesV1.AudienceCommercialBillingRole}
              AND {LicenseNoticesV1.ChannelInApp} = ANY(j.allowed_channels)
              AND j.effective_deadline_at > {now}
            """).SingleAsync(ct);

    public async Task<ErrorOr<NotificationReadPage>> ReadProviderAccountJobsAsync(
        Guid accountId,
        Guid? after,
        int take,
        CancellationToken ct)
    {
        DateTimeOffset? cursorDue = null;
        if (after is { } cursorId)
        {
            var found = await db.Database.SqlQuery<DateTimeOffset>($"""
                SELECT due_at AS "Value"
                FROM public.effective_notification_jobs
                WHERE account_id = {accountId} AND id = {cursorId}
                """).ToListAsync(ct);
            if (found is not [var value])
                return NotificationErrors.InvalidCursor;
            cursorDue = value;
        }

        var rows = await db.Database.SqlQuery<JobRow>($"""
            SELECT id, account_id, scope_kind, scope_id, recipient_key, recipient_user_id, contact_id,
                audience, purpose, source_kind, source_id, source_revision, threshold,
                due_at, effective_deadline_at, time_zone, locale, allowed_channels,
                status, template_key, template_arguments::text AS template_arguments,
                destination_hint, planned_at
            FROM public.effective_notification_jobs
            WHERE account_id = {accountId}
              AND ({after}::uuid IS NULL OR (due_at, id) > ({cursorDue}, {after}::uuid))
            ORDER BY due_at ASC, id ASC
            LIMIT {take + 1}
            """).ToListAsync(ct);

        return ToPage(rows, take, nextDue: null);
    }

    private static CurrentCommercialSource? BuildCurrentCommercialSource(
        bool isAccountSuspended,
        TermPlanSource termSource)
    {
        var facts = SubscriptionStatusProjection.ToFacts(termSource.Term);
        if (facts.Evidence != TermEvidenceState.Complete || facts.Basis is null || facts.RenewedAtEnd)
            return null;

        var activeShops = termSource.Shops.Where(shop => !shop.IsClosed).ToArray();
        if (termSource.Shops.Count > 0 && activeShops.Length == 0)
            return null;

        var restriction = NotificationPlanner.ResolveRestrictionScope(isAccountSuspended, activeShops, out var suspendedCount);
        var cancelVersion = SubscriptionStatusProjection.ActiveCancellation(termSource.Term)?.Version ?? 0L;
        var revision = NotificationSchedulePolicy.BuildCommercialSourceRevision(
            facts.Basis.Value.ToString(),
            termSource.Term.TermId,
            facts,
            termSource.TrialExtensionRevision,
            cancelVersion,
            termSource.Term.PricePaise,
            termSource.Term.Currency);
        var sample = LicenseNoticesV1.BoundShopNameSample(activeShops.Select(shop => shop.ShopName));
        return new CurrentCommercialSource(
            termSource.Term.TermId,
            revision,
            restriction,
            activeShops.Length,
            suspendedCount,
            sample,
            activeShops.Length > sample.Length);
    }

    private static List<JobRow> SliceAfterCursor(List<JobRow> orderedRows, Guid? after, int take, out bool hasMore)
    {
        var startIndex = 0;
        if (after is { } cursorId)
        {
            var found = orderedRows.FindIndex(row => row.Id == cursorId);
            if (found < 0)
            {
                hasMore = false;
                return [];
            }

            startIndex = found + 1;
        }

        var remaining = orderedRows.Skip(startIndex).Take(take + 1).ToList();
        hasMore = remaining.Count > take;
        return remaining.Take(take).ToList();
    }

    private static NotificationReadPage ToPage(
        List<JobRow> rows,
        int take,
        DateTimeOffset? nextDue)
    {
        var hasMore = rows.Count > take;
        var items = rows.Take(take).Select(MapRow).ToArray();
        return new NotificationReadPage(items, nextDue?.ToUniversalTime(), hasMore);
    }

    private static NotificationJobReadRow MapRow(JobRow row) => new(
        row.Id,
        row.AccountId,
        row.ScopeKind,
        row.ScopeId,
        row.RecipientKey,
        row.RecipientUserId,
        row.ContactId,
        row.Audience,
        row.Purpose,
        row.SourceKind,
        row.SourceId,
        row.SourceRevision,
        row.Threshold,
        row.DueAt.ToUniversalTime(),
        row.EffectiveDeadlineAt.ToUniversalTime(),
        row.TimeZone,
        row.Locale,
        row.AllowedChannels,
        row.Status,
        row.TemplateKey,
        row.TemplateArguments,
        row.DestinationHint,
        row.PlannedAt.ToUniversalTime());

    private sealed record JobRow(
        Guid Id,
        Guid AccountId,
        string ScopeKind,
        Guid ScopeId,
        string RecipientKey,
        Guid? RecipientUserId,
        Guid? ContactId,
        string Audience,
        string Purpose,
        string SourceKind,
        Guid SourceId,
        string SourceRevision,
        string Threshold,
        DateTimeOffset DueAt,
        DateTimeOffset EffectiveDeadlineAt,
        string TimeZone,
        string Locale,
        string[] AllowedChannels,
        string Status,
        string TemplateKey,
        string TemplateArguments,
        string? DestinationHint,
        DateTimeOffset PlannedAt);
}
