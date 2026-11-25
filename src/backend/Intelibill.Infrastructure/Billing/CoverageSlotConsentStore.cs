using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Domain.Policies;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Licensing;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Owner = Intelibill.Infrastructure.Billing.ShopAffiliationConsentStore.LiveOwner;

namespace Intelibill.Infrastructure.Billing;

internal sealed class CoverageSlotConsentStore(ApplicationDbContext db, ShopAffiliationConsentStore authority, ShopLicenceFence fence,
    TimeProvider transportClock, [FromKeyedServices(ShopTrialsV1.DeliverySection)] TimeProvider businessClock)
{
    internal async Task<ErrorOr<CoverageSlotOwnerV1>> RunAsync(MerchantBillingCaller caller, CoverageSlotAssignmentV1 row,
        ShopAffiliationConsentAction action, Guid key, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var entered = await fence.EnterProjectionAsync(row.AccountId, row.ShopId, ct);
        if (entered.IsError) return entered.Errors;
        if (await authority.LockOwnerAsync(caller, row.ShopId, ct) is not { } owner) return ShopAffiliationConsentErrors.OwnerRequired;
        var now = EntitlementSnapshotsV1.Instant(businessClock);
        var events = await db.Database.SqlQuery<ConsentEvent>($"""
            SELECT assignment_id,actor_id,membership_id,authority_version,kind,operation_key FROM public.coverage_slot_consent_events
            WHERE shop_id={row.ShopId} AND (assignment_id={row.AssignmentId} OR (actor_id={owner.ActorId} AND operation_key={key}))
            """).ToArrayAsync(ct);
        var prior = events.Where(e => e.AssignmentId == row.AssignmentId).ToArray();
        var status = Status(prior, owner, row, now);
        if (action == ShopAffiliationConsentAction.Read)
            return new CoverageSlotOwnerV1(row, await ReadStatusAsync(row, status, now, ct));
        var kind = action switch
        {
            ShopAffiliationConsentAction.Grant => "Granted",
            ShopAffiliationConsentAction.Revoke => "Revoked",
            ShopAffiliationConsentAction.Accept => "Accepted",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        if (events.SingleOrDefault(e => e.ActorId == owner.ActorId && e.OperationKey == key) is { } replay)
            return replay.AssignmentId == row.AssignmentId && replay.Kind == kind && replay.Matches(owner)
                ? new CoverageSlotOwnerV1(row, status) : MerchantBillingErrors.PayloadMismatch;
        if (row.State != "AwaitingConsent" || now < row.CreatedAt || now >= row.ExpiresAt ||
            (kind == "Granted" ? status != "AwaitingConsent" : status != "Granted"))
            return ShopAffiliationConsentErrors.ConsentInvalid;
        var seats = await SeatsAsync(row, now, ct);
        if (seats is null) return ShopAffiliationConsentErrors.ConsentInvalid;
        if (kind == "Accepted" && seats.ActiveMembers > row.MemberAllowance) return Error.Conflict("Coverage.MemberCapacityExceeded");
        await SaveAsync(row, owner, kind, key, now, seats, ct);
        await tx.CommitAsync(ct);
        return new CoverageSlotOwnerV1(row, kind);
    }

    private async Task<string> ReadStatusAsync(CoverageSlotAssignmentV1 row, string status, DateTimeOffset now, CancellationToken ct)
    {
        if (row.State == "Refused") return "NeedsReview";
        if (row.State is "Expired" or "Stale") return row.State;
        if (row.State != "Applied") return status;
        return row.Activation switch
        {
            "Closed" => "Closed",
            "TermEnded" => "Expired",
            "Suspended" => "Suspended",
            "NeedsReview" => "NeedsReview",
            "Grace" => "Grace",
            "Acknowledged" => await CheckLocalSuspensionAsync(row, now, ct) ?? "Acknowledged",
            _ => await LocalPendingOutcomeAsync(row, now, ct),
        };
    }

    private async Task<string?> CheckLocalSuspensionAsync(CoverageSlotAssignmentV1 row, DateTimeOffset now, CancellationToken ct)
    {
        var heads = await SuspensionAccessReader.ReadHeadsAsync(db, row.ShopId, ct);
        return heads is not null && LicenseProjectionPolicy.EffectiveSuspension(heads.Shop, heads.Account, now) is not null
            ? "Suspended" : null;
    }

    private async Task<string> LocalPendingOutcomeAsync(CoverageSlotAssignmentV1 row, DateTimeOffset now, CancellationToken ct)
    {
        var activated = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.coverage_slot_activations
                WHERE assignment_id = {row.AssignmentId} AND shop_id = {row.ShopId}) AS "Value"
            """).SingleAsync(ct);
        if (activated)
        {
            if (await CheckLocalSuspensionAsync(row, now, ct) is { } suspended) return suspended;
            var localState = await db.Database.SqlQuery<string?>($"""
                SELECT state AS "Value" FROM public.shop_license_states WHERE shop_id = {row.ShopId}
                """).SingleOrDefaultAsync(ct);
            return localState == "Grace" ? "Grace" : "AppliedLocally";
        }
        var rejectedLocally = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT FROM public.entitlement_snapshot_inbox i
                JOIN public.license_grant_evidence e ON e.event_id = i.event_id
                WHERE i.shop_id = {row.ShopId} AND i.account_id = {row.AccountId}
                  AND convert_from(i.acknowledgement, 'UTF8')::jsonb->>'outcome' = 'NeedsReview'
                  AND (convert_from(e.payload, 'UTF8')::jsonb->'shop'->>'slotAssignmentId' = {row.AssignmentId.ToString("D")}
                       OR convert_from(e.payload, 'UTF8')::jsonb->'shop'->>'termId' = {row.TermId.ToString("D")})
            ) AS "Value"
            """).SingleAsync(ct);
        return rejectedLocally ? "NeedsReview" : "Applying";
    }

    private Task<Seats?> SeatsAsync(CoverageSlotAssignmentV1 row, DateTimeOffset now, CancellationToken ct) =>
        db.Database.SqlQuery<Seats>($"""
            SELECT (SELECT count(*)::int FROM public.shop_memberships WHERE shop_id=s.shop_id AND status='Active') AS active_members,
                (SELECT encode(sha256(convert_to(string_agg(id::text||'/'||authority_version::text,',' ORDER BY id),'UTF8')),'hex')
                    FROM public.shop_memberships WHERE shop_id=s.shop_id) AS seat_hash
            FROM public.shop_license_states s WHERE s.shop_id={row.ShopId} AND s.account_id={row.AccountId}
                AND s.affiliation_request_id={row.AffiliationRequestId} AND s.state<>'Closed'
                AND (s.term_id IS NULL OR s.access_ends_at<={now})
            """).SingleOrDefaultAsync(ct);

    private async Task SaveAsync(CoverageSlotAssignmentV1 row, Owner owner, string kind, Guid key, DateTimeOffset now, Seats seats, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.coverage_slot_consent_events(id,shop_id,account_id,assignment_id,affiliation_request_id,term_id,
                actor_id,membership_id,authority_version,operation_key,kind,recorded_at,expires_at,coverage_ends_at,
                member_allowance,active_members,seat_hash)
            VALUES({id},{row.ShopId},{row.AccountId},{row.AssignmentId},{row.AffiliationRequestId},{row.TermId},{owner.ActorId},
                {owner.MembershipId},{owner.Version},{key},{kind},{now},{row.ExpiresAt},{row.CoverageEndsAt},{row.MemberAllowance},
                {(kind == "Accepted" ? (int?)seats.ActiveMembers : null)}::int,{(kind == "Accepted" ? seats.SeatHash : null)}::text)
            """, ct);
        if (kind != "Accepted") return;
        var receipt = new CoverageSlotConsentV1(1, id, row.AssignmentId, row.AccountId, row.TermId, row.ShopId, row.AffiliationRequestId,
            owner.ActorId, owner.MembershipId, owner.Version, seats.ActiveMembers, seats.SeatHash, row.MemberAllowance, row.CoverageEndsAt, now);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, EntitlementSnapshotsV1.Json);
        await new LeasedHttpOutbox(db).AppendAsync(id, row.AccountId, row.ShopId, CoverageSlotsV1.Kind, bytes,
            SignedServiceAssertion.Digest(bytes), EntitlementSnapshotsV1.Instant(transportClock), ct);
    }

    private static string Status(ConsentEvent[] events, Owner owner, CoverageSlotAssignmentV1 row, DateTimeOffset now)
    {
        if (events.Any(e => e.Kind == "Accepted")) return "Accepted";
        if (events.Any(e => e.Kind == "Revoked")) return "Revoked";
        if (row.State is "Expired" or "Stale") return row.State;
        if (now >= row.ExpiresAt) return "Expired";
        var grant = events.SingleOrDefault(e => e.Kind == "Granted");
        return grant is null ? "AwaitingConsent" : grant.Matches(owner) ? "Granted" : "Stale";
    }

    private sealed record Seats(int ActiveMembers, string SeatHash);
    private sealed record ConsentEvent(Guid AssignmentId, Guid ActorId, Guid MembershipId, long AuthorityVersion, string Kind, Guid OperationKey)
    {
        internal bool Matches(Owner owner) => ActorId == owner.ActorId && MembershipId == owner.MembershipId && AuthorityVersion == owner.Version;
    }
}
