using Intelibill.Licensing.Application.Entitlements;
using System.Text.Json;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Licensing.Infrastructure.Status;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Infrastructure.Entitlements;

/// <summary>Freezes source facts inside the writer's existing account-fenced transaction.</summary>
internal sealed partial class EntitlementPublisher(LicensingDbContext db, ISubscriptionAccessFactsReader reader,
    [FromKeyedServices(SubscriptionStatusRegistration.ClockKey)] TimeProvider lifecycle, TimeProvider deliveryClock)
    : IEntitlementPublisher
{
    internal DateTimeOffset Now => EntitlementSnapshotsV1.Instant(lifecycle);

    public async Task PublishAsync(Guid account, Guid shop, CancellationToken ct, DateTimeOffset? evaluatedAt = null)
    {
        if (!await PublishAsync(account, shop, reconcile: false, ct, evaluatedAt))
            throw new InvalidOperationException("A committed entitlement source requires an affiliation.");
    }

    internal async Task<bool> PublishAsync(Guid account, Guid shop, bool reconcile, CancellationToken ct,
        DateTimeOffset? evaluatedAt = null) => await AppendSnapshotAsync(account, shop, reconcile, evaluatedAt, ct) is not null;

    internal Task<EntitlementSnapshotV1> PublishSuspensionAsync(Guid account, Guid shop, DateTimeOffset at, CancellationToken ct) =>
        PublishShopChangeAsync(account, shop, at, ct);

    internal async Task<EntitlementSnapshotV1> PublishShopChangeAsync(Guid account, Guid shop, DateTimeOffset at, CancellationToken ct) =>
        await AppendSnapshotAsync(account, shop, false, at, ct)
        ?? throw new InvalidOperationException("A shop change requires the selected affiliation.");

    internal async Task PublishClosureAsync(Guid account, Guid shop, Guid eventId, CancellationToken ct)
    {
        if (await AppendSnapshotAsync(account, shop, false, null, ct, eventId) is null)
            throw new InvalidOperationException("Closure publication requires an affiliation.");
    }

    internal async Task PublishReopeningAsync(Guid account, Guid shop, DateTimeOffset at, Guid eventId, CancellationToken ct)
    {
        if (await AppendSnapshotAsync(account, shop, false, at, ct, eventId) is null)
            throw new InvalidOperationException("Reopening publication requires an affiliation.");
    }

    private async Task<EntitlementSnapshotV1?> AppendSnapshotAsync(Guid account, Guid shop, bool reconcile,
        DateTimeOffset? evaluatedAt, CancellationToken ct, Guid? eventId = null)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Entitlement publication requires the owning transaction.");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('app.status_shop_id', {shop.ToString("D")}, true)
            """, ct);
        var now = evaluatedAt is { } at ? new DateTimeOffset(at.UtcTicks - at.UtcTicks % 10, TimeSpan.Zero) : Now;
        var request = await db.Database.SqlQuery<Guid>($"""
            SELECT request_id AS "Value" FROM public.current_shop_affiliations WHERE shop_id = {shop} AND account_id = {account}
            """).SingleOrDefaultAsync(ct);
        if (request == Guid.Empty || await reader.ReadShopAsync(shop, ct, now) is not { } coverage || coverage.AccountId != account)
            return null;

        var accountHead = await ReadAccountAsync(account, reconcile, now, ct);
        var term = SubscriptionStatusProjection.Choose(coverage, now, commercial: false);
        var next = NextBoundary(coverage, now, term?.NextDeadline);
        var replacement = await ReadReplacementAsync(account, shop, request, term?.TermId, now, ct);
        if (replacement.NextStart is { } start) next = Earlier(next, start);
        if (replacement.ExceptionNext is { } exNext) next = Earlier(next, exNext);
        var nextVerification = Earlier(next, Earlier(accountHead.UpstreamVerifiedAt.AddHours(47), now.AddHours(47)));
        var revision = await NextShopRevisionAsync(account, shop, nextVerification, ct);
        var suspension = await db.Database.SqlQuery<CustomerSuspensionV1>($"""
            SELECT mode, effective_at, customer_explanation FROM public.active_operational_restrictions
            WHERE account_id = {account} AND shop_id = {shop}
            """).SingleOrDefaultAsync(ct);
        var released = await db.Database.SqlQuery<long?>($"""
            SELECT max(l.prior_revision) AS "Value"
            FROM public.operational_restriction_releases l
            JOIN public.operational_restrictions r ON r.id = l.restriction_id
            WHERE l.account_id = {account} AND r.shop_id = {shop}
            """).SingleAsync(ct);
        var closure = await ReadClosureAsync(account, shop, request, eventId, revision, ct);
        var state = closure.Closed ? ShopClosuresV1.Closed : term?.State ?? "PendingActivation";
        var slotAssignmentId = !closure.Closed && term is not null && coverage.Term is { } baseline && term.TermId == baseline.TermId
            ? baseline.SlotAssignmentId : null;
        var shopHead = new ShopEntitlementSnapshotV1(revision, now, state, term?.TermId,
            term?.Basis, term?.StartsAt, term?.AccessEndsAt, next, term?.PurchasedCapacity, term?.MemberAllowance,
            suspension, released, closure.OpeningCutoff, closure.Revision, slotAssignmentId, replacement.Incoming, replacement.Released,
            !closure.Closed ? replacement.ExceptionId : null,
            !closure.Closed ? replacement.ExceptionLeg : null,
            !closure.Closed ? replacement.ExceptionGeneration : null);
        var snapshot = new EntitlementSnapshotV1(1, EntitlementSnapshotsV1.PolicyVersion, eventId ?? Guid.NewGuid(), account,
            shop, request, accountHead, shopHead);
        await AppendAsync(snapshot, ct);
        return snapshot;
    }

    private async Task<AccountRestrictionSnapshotV1> ReadAccountAsync(Guid account, bool reconcile,
        DateTimeOffset now, CancellationToken ct)
    {
        // Refresh verification without replacing the restriction set by the provider writer.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.entitlement_account_heads (account_id, revision, upstream_verified_at, restriction)
            VALUES ({account}, 1, {now}, 'Unrestricted')
            ON CONFLICT (account_id) DO UPDATE SET revision = entitlement_account_heads.revision + 1,
                upstream_verified_at = EXCLUDED.upstream_verified_at
            WHERE {reconcile} AND entitlement_account_heads.upstream_verified_at < EXCLUDED.upstream_verified_at
            """, ct);
        var row = await db.Database.SqlQuery<AccountHead>($"""
            SELECT h.revision, h.upstream_verified_at, h.restriction, h.released_revision,
                r.effective_at, r.customer_explanation, h.previous_release_revision
            FROM public.entitlement_account_heads h
            LEFT JOIN public.active_operational_restrictions r ON r.account_id = h.account_id AND r.shop_id IS NULL
            WHERE h.account_id = {account}
            """).SingleAsync(ct);
        return new(row.Revision, row.UpstreamVerifiedAt, row.Restriction, row.ReleasedRevision,
            row.EffectiveAt is { } at ? new(row.Restriction, at, row.CustomerExplanation!) : null, row.PreviousReleaseRevision);
    }

    private async Task<long> NextShopRevisionAsync(Guid account, Guid shop, DateTimeOffset due, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<long>($"""
            INSERT INTO public.entitlement_shop_heads (shop_id, account_id, revision, next_verification_at)
            VALUES ({shop}, {account}, 1, {due})
            ON CONFLICT (shop_id) DO UPDATE SET revision = entitlement_shop_heads.revision + 1,
                next_verification_at = EXCLUDED.next_verification_at
            WHERE entitlement_shop_heads.account_id = EXCLUDED.account_id
            RETURNING revision AS "Value"
            """).ToListAsync(ct);
        return rows.Single();
    }

    private async Task AppendAsync(EntitlementSnapshotV1 snapshot, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, EntitlementSnapshotsV1.Json);
        await new LeasedHttpOutbox(db).AppendAsync(snapshot.EventId, snapshot.AccountId, snapshot.ShopId, "Snapshot",
            bytes, SignedServiceAssertion.Digest(bytes), EntitlementSnapshotsV1.Instant(deliveryClock), ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.entitlement_snapshots
                (event_id, account_id, shop_id, affiliation_request_id, account_revision, shop_revision,
                 closure_revision, publisher_protocol)
            VALUES ({snapshot.EventId}, {snapshot.AccountId}, {snapshot.ShopId}, {snapshot.AffiliationRequestId},
                {snapshot.Account!.Revision}, {snapshot.Shop!.Revision}, {snapshot.Shop.ClosureRevision}, 2)
            """, ct);
    }

    private static DateTimeOffset? NextBoundary(ShopCoverage coverage, DateTimeOffset now, DateTimeOffset? selectedDeadline)
    {
        IEnumerable<TermFactsRow> rows = coverage.Grants ?? [];
        if (coverage.Term is { } baseline) rows = rows.Prepend(baseline);
        return rows.SelectMany(row =>
            {
                var status = SubscriptionStatusProjection.Project(row, now, commercial: false);
                return new DateTimeOffset?[] { status.StartsAt, status.PaidThrough, status.GraceEndsAt, status.NextDeadline };
            }).Append(selectedDeadline).Append(coverage.ConvertedPaidStartsAt).Where(date => date > now).Min();
    }

    private sealed record AccountHead(long Revision, DateTimeOffset UpstreamVerifiedAt, string Restriction,
        long? ReleasedRevision, DateTimeOffset? EffectiveAt, string? CustomerExplanation, long? PreviousReleaseRevision);

    private static DateTimeOffset Earlier(DateTimeOffset? first, DateTimeOffset other) =>
        first is { } value && value < other ? value : other;
}
