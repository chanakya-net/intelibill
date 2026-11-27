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

internal sealed class ReplacementConsentStore(ApplicationDbContext db, ShopAffiliationConsentStore authority, ShopLicenceFence fence,
    TimeProvider transportClock, [FromKeyedServices(ShopTrialsV1.DeliverySection)] TimeProvider businessClock)
{
    internal async Task<ErrorOr<ReplacementOwnerV1>> RunAsync(MerchantBillingCaller caller, CoverageReplacementV1 row,
        ShopAffiliationConsentAction action, Guid key, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var entered = await fence.EnterProjectionAsync(row.AccountId, row.IncomingShopId, ct);
        if (entered.IsError) return entered.Errors;
        if (await authority.LockOwnerAsync(caller, row.IncomingShopId, ct) is not { } owner) return ShopAffiliationConsentErrors.OwnerRequired;
        var now = EntitlementSnapshotsV1.Instant(businessClock);
        var events = await db.Database.SqlQuery<ConsentEvent>($"""
            SELECT replacement_id,actor_id,membership_id,authority_version,kind,operation_key FROM public.coverage_replacement_consent_events
            WHERE shop_id={row.IncomingShopId} AND (replacement_id={row.ReplacementId} OR (actor_id={owner.ActorId} AND operation_key={key}))
            """).ToArrayAsync(ct);
        var prior = events.Where(e => e.ReplacementId == row.ReplacementId).ToArray();
        var status = Status(prior, owner, row, now);
        if (action == ShopAffiliationConsentAction.Read)
            return View(row, status);
        var kind = action switch
        {
            ShopAffiliationConsentAction.Grant => "Granted",
            ShopAffiliationConsentAction.Revoke => "Revoked",
            ShopAffiliationConsentAction.Accept => "Accepted",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        if (events.SingleOrDefault(e => e.ActorId == owner.ActorId && e.OperationKey == key) is { } replay)
            return replay.ReplacementId == row.ReplacementId && replay.Kind == kind && replay.Matches(owner)
                ? View(row, status) : MerchantBillingErrors.PayloadMismatch;
        var allowed = kind switch
        {
            "Granted" => row.State == "AwaitingConsent" && status == "AwaitingConsent",
            "Accepted" => row.State == "AwaitingConsent" && status == "Granted",
            _ => row.State is "AwaitingConsent" or "Scheduled" or "Applied" && status is "Granted" or "Accepted"
                && !await ActivatedAsync(row, ct),
        };
        if (!allowed || now < row.CreatedAt || (kind != "Revoked" && now >= row.ExpiresAt))
            return ShopAffiliationConsentErrors.ConsentInvalid;
        var seats = await SeatsAsync(row, now, kind == "Revoked", ct);
        if (seats is null) return ShopAffiliationConsentErrors.ConsentInvalid;
        if (kind == "Accepted" && seats.ActiveMembers > row.MemberAllowance) return Error.Conflict("Coverage.MemberCapacityExceeded");
        await SaveAsync(row, owner, kind, key, now, seats, ct);
        await tx.CommitAsync(ct);
        return View(row, kind);
    }

    private static ReplacementOwnerV1 View(CoverageReplacementV1 row, string status) =>
        new(row.ReplacementId, row.IncomingShopId, row.IncomingShopName, row.EffectiveAt, row.State, status, row.Activation);

    private Task<bool> ActivatedAsync(CoverageReplacementV1 row, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM public.coverage_replacement_activations WHERE replacement_id={row.ReplacementId}) AS "Value"
            """).SingleAsync(ct);

    private Task<Seats?> SeatsAsync(CoverageReplacementV1 row, DateTimeOffset now, bool revoking, CancellationToken ct) =>
        db.Database.SqlQuery<Seats>($"""
            SELECT (SELECT count(*)::int FROM public.shop_memberships WHERE shop_id=s.shop_id AND status='Active') AS active_members,
                (SELECT encode(sha256(convert_to(string_agg(id::text||'/'||authority_version::text,',' ORDER BY id),'UTF8')),'hex')
                    FROM public.shop_memberships WHERE shop_id=s.shop_id) AS seat_hash
            FROM public.shop_license_states s WHERE s.shop_id={row.IncomingShopId} AND s.account_id={row.AccountId}
                AND s.affiliation_request_id={row.IncomingAffiliationId} AND ({revoking} OR (s.state<>'Closed'
                    AND (s.term_id IS NULL OR s.access_ends_at<={now})))
            """).SingleOrDefaultAsync(ct);

    private async Task SaveAsync(CoverageReplacementV1 row, Owner owner, string kind, Guid key, DateTimeOffset now, Seats seats, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.coverage_replacement_consent_events(id,shop_id,account_id,replacement_id,affiliation_request_id,term_id,
                actor_id,membership_id,authority_version,operation_key,kind,recorded_at,expires_at,effective_at,
                member_allowance,active_members,seat_hash,outgoing_shop_id)
            VALUES({id},{row.IncomingShopId},{row.AccountId},{row.ReplacementId},{row.IncomingAffiliationId},{row.TermId},{owner.ActorId},
                {owner.MembershipId},{owner.Version},{key},{kind},{now},{row.ExpiresAt},{row.EffectiveAt},{row.MemberAllowance},
                {(kind == "Accepted" ? (int?)seats.ActiveMembers : null)}::int,{(kind == "Accepted" ? seats.SeatHash : null)}::text,{row.OutgoingShopId})
            """, ct);
        if (kind == "Granted") return;
        var receipt = new ReplacementConsentV1(1, id, row.ReplacementId, row.AccountId, row.TermId, row.IncomingShopId, row.IncomingAffiliationId,
            owner.ActorId, owner.MembershipId, owner.Version, seats.ActiveMembers, seats.SeatHash, row.MemberAllowance, row.EffectiveAt, now, kind);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, EntitlementSnapshotsV1.Json);
        await new LeasedHttpOutbox(db).AppendAsync(id, row.AccountId, row.IncomingShopId, CoverageReplacementsV1.Kind, bytes,
            SignedServiceAssertion.Digest(bytes), EntitlementSnapshotsV1.Instant(transportClock), ct);
    }

    private static string Status(ConsentEvent[] events, Owner owner, CoverageReplacementV1 row, DateTimeOffset now)
    {
        if (events.Any(e => e.Kind == "Revoked")) return "Revoked";
        if (events.Any(e => e.Kind == "Accepted")) return "Accepted";
        if (row.State is "Expired" or "Stale") return row.State;
        if (now >= row.ExpiresAt) return "Expired";
        var grant = events.SingleOrDefault(e => e.Kind == "Granted");
        return grant is null ? "AwaitingConsent" : grant.Matches(owner) ? "Granted" : "Stale";
    }

    private sealed record Seats(int ActiveMembers, string SeatHash);
    private sealed record ConsentEvent(Guid ReplacementId, Guid ActorId, Guid MembershipId, long AuthorityVersion, string Kind, Guid OperationKey)
    {
        internal bool Matches(Owner owner) => ActorId == owner.ActorId && MembershipId == owner.MembershipId && AuthorityVersion == owner.Version;
    }
}
