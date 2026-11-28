using ErrorOr;
using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Notifications;

internal sealed class NotificationJobReader(LicensingDbContext db) : ILicenseNoticeReader
{
    public async Task<Guid?> ReadCurrentShopAccountAsync(Guid shopId, CancellationToken ct)
    {
        var accounts = await db.Database.SqlQuery<Guid>($"""
            SELECT account_id AS "Value"
            FROM public.current_shop_affiliations
            WHERE shop_id = {shopId}
            """).ToListAsync(ct);
        return accounts is [var accountId] ? accountId : null;
    }

    public async Task<NotificationReadPage> ReadDueShopNoticesAsync(
        Guid accountId,
        Guid shopId,
        Guid actorId,
        DateTimeOffset now,
        Guid? after,
        int take,
        CancellationToken ct)
    {
        DateTimeOffset? cursorDue = null;
        if (after is { } cursorId)
        {
            cursorDue = await ReadCursorDueAtAsync(
                accountId,
                LicenseNoticesV1.ScopeShop,
                shopId,
                actorId,
                cursorId,
                ct);
            if (cursorDue is null)
                return new NotificationReadPage([], null, false);
        }

        var rows = await db.Database.SqlQuery<JobRow>($"""
            SELECT id, account_id, scope_kind, scope_id, recipient_key, recipient_user_id, contact_id,
                audience, purpose, source_kind, source_id, source_revision, threshold,
                due_at, effective_deadline_at, time_zone, locale, allowed_channels,
                status, template_key, template_arguments::text AS template_arguments,
                destination_hint, planned_at
            FROM public.notification_jobs
            WHERE account_id = {accountId}
              AND scope_kind = {LicenseNoticesV1.ScopeShop}
              AND scope_id = {shopId}
              AND recipient_user_id = {actorId}
              AND due_at <= {now}
              AND ({after}::uuid IS NULL OR (due_at, id) > ({cursorDue}, {after}::uuid))
            ORDER BY due_at ASC, id ASC
            LIMIT {take + 1}
            """).ToListAsync(ct);

        var nextDue = await db.Database.SqlQuery<DateTimeOffset?>($"""
            SELECT MIN(due_at) AS "Value"
            FROM public.notification_jobs
            WHERE account_id = {accountId}
              AND scope_kind = {LicenseNoticesV1.ScopeShop}
              AND scope_id = {shopId}
              AND recipient_user_id = {actorId}
              AND due_at > {now}
            """).SingleAsync(ct);

        return ToPage(rows, take, nextDue);
    }

    public async Task<NotificationReadPage> ReadDueAccountNoticesAsync(
        Guid accountId,
        Guid actorId,
        DateTimeOffset now,
        Guid? after,
        int take,
        CancellationToken ct)
    {
        DateTimeOffset? cursorDue = null;
        if (after is { } cursorId)
        {
            cursorDue = await ReadCursorDueAtAsync(
                accountId,
                LicenseNoticesV1.ScopeAccount,
                accountId,
                actorId,
                cursorId,
                ct);
            if (cursorDue is null)
                return new NotificationReadPage([], null, false);
        }

        var rows = await db.Database.SqlQuery<JobRow>($"""
            SELECT id, account_id, scope_kind, scope_id, recipient_key, recipient_user_id, contact_id,
                audience, purpose, source_kind, source_id, source_revision, threshold,
                due_at, effective_deadline_at, time_zone, locale, allowed_channels,
                status, template_key, template_arguments::text AS template_arguments,
                destination_hint, planned_at
            FROM public.notification_jobs
            WHERE account_id = {accountId}
              AND scope_kind = {LicenseNoticesV1.ScopeAccount}
              AND scope_id = {accountId}
              AND recipient_user_id = {actorId}
              AND due_at <= {now}
              AND ({after}::uuid IS NULL OR (due_at, id) > ({cursorDue}, {after}::uuid))
            ORDER BY due_at ASC, id ASC
            LIMIT {take + 1}
            """).ToListAsync(ct);

        var nextDue = await db.Database.SqlQuery<DateTimeOffset?>($"""
            SELECT MIN(due_at) AS "Value"
            FROM public.notification_jobs
            WHERE account_id = {accountId}
              AND scope_kind = {LicenseNoticesV1.ScopeAccount}
              AND scope_id = {accountId}
              AND recipient_user_id = {actorId}
              AND due_at > {now}
            """).SingleAsync(ct);

        return ToPage(rows, take, nextDue);
    }

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
                FROM public.notification_jobs
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
            FROM public.notification_jobs
            WHERE account_id = {accountId}
              AND ({after}::uuid IS NULL OR (due_at, id) > ({cursorDue}, {after}::uuid))
            ORDER BY due_at ASC, id ASC
            LIMIT {take + 1}
            """).ToListAsync(ct);

        return ToPage(rows, take, nextDue: null);
    }

    private async Task<DateTimeOffset?> ReadCursorDueAtAsync(
        Guid accountId,
        string scopeKind,
        Guid scopeId,
        Guid actorId,
        Guid cursorId,
        CancellationToken ct)
    {
        var found = await db.Database.SqlQuery<DateTimeOffset>($"""
            SELECT due_at AS "Value"
            FROM public.notification_jobs
            WHERE account_id = {accountId}
              AND scope_kind = {scopeKind}
              AND scope_id = {scopeId}
              AND recipient_user_id = {actorId}
              AND id = {cursorId}
            """).ToListAsync(ct);
        return found is [var value] ? value : null;
    }

    private static NotificationReadPage ToPage(
        List<JobRow> rows,
        int take,
        DateTimeOffset? nextDue)
    {
        var hasMore = rows.Count > take;
        var items = rows.Take(take).Select(row => new NotificationJobReadRow(
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
            row.PlannedAt.ToUniversalTime())).ToArray();
        return new NotificationReadPage(items, nextDue?.ToUniversalTime(), hasMore);
    }

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
