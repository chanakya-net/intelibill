using System.Text.Json;
using Intelibill.Licensing.Application.Subscriptions;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Licensing.Infrastructure.Grants;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Infrastructure.Entitlements;

internal sealed partial class CoverageExceptionAcknowledgements(
    LicensingDbContext db,
    ICapacityChangeQuoteStore terms,
    ComplimentaryGrantStore grants,
    EntitlementPublisher publisher,
    [FromKeyedServices(CheckoutOrdersV1.Policy)] TimeProvider businessClock,
    TimeProvider deliveryClock)
{
    internal async Task<bool> ApplyConsentAsync(CoverageReplacementExceptionConsentV1 c, CancellationToken ct)
    {
        var now = EntitlementSnapshotsV1.Instant(businessClock);
        var row = await db.Database.SqlQuery<ExceptionConsentRow>($"""
            SELECT exception_id, account_id, term_id, proposal_revision, terms_digest,
                outgoing_shop_id, outgoing_affiliation_id, incoming_shop_id, incoming_affiliation_id,
                member_allowance, requested_early_start_at, paid_cutover_at, normal_renewal_at,
                overlap_disposition, created_at, expires_at, source_hash, state,
                outgoing_consent_event, outgoing_revoke_event, incoming_consent_event, incoming_revoke_event
            FROM public.coverage_replacement_exceptions
            WHERE account_id = {c.AccountId} AND exception_id = {c.ExceptionId} AND term_id = {c.TermId}
                AND proposal_revision = {c.ProposalRevision} AND terms_digest = {c.TermsDigest}
                AND member_allowance = {c.MemberAllowance}
                AND requested_early_start_at = {c.RequestedEarlyStartAt}
                AND paid_cutover_at = {c.PaidCutoverAt} AND normal_renewal_at = {c.NormalRenewalAt}
                AND overlap_disposition = {c.OverlapDisposition}
            """).SingleOrDefaultAsync(ct);
        if (row is null || c.RecordedAt < row.CreatedAt || c.RecordedAt > now
            || (c.Kind != "Revoked" && (c.RecordedAt >= row.ExpiresAt || c.RecordedAt >= row.PaidCutoverAt)))
            return false;

        var isOutgoing = c.Leg == CoverageReplacementExceptionsV1.OutgoingLeg;
        var expectedShop = isOutgoing ? row.OutgoingShopId : row.IncomingShopId;
        var expectedAff = isOutgoing ? row.OutgoingAffiliationId : row.IncomingAffiliationId;
        if (c.ShopId != expectedShop || c.AffiliationRequestId != expectedAff)
            return false;
        if (row.State is "Applied" or "Refused" or "Blocked" or "Consuming" or "Aborting")
            return true;

        return c.Kind == "Revoked"
            ? await ApplyRevocationAsync(row, c, isOutgoing, now, ct)
            : await ApplyAcceptanceAsync(row, c, isOutgoing, now, ct);
    }

    private async Task<bool> ApplyRevocationAsync(ExceptionConsentRow row, CoverageReplacementExceptionConsentV1 c,
        bool isOutgoing, DateTimeOffset now, CancellationToken ct)
    {
        if (row.State == "Preparing")
            return await AbortPreparingForRevocationAsync(row, c, isOutgoing, now, ct);

        var updated = isOutgoing
            ? await db.Database.ExecuteSqlAsync($"""
                UPDATE public.coverage_replacement_exceptions
                SET outgoing_revoke_event = {c.EventId}, state = 'Blocked', reason = 'ConsentRevoked'
                WHERE account_id = {row.AccountId} AND exception_id = {row.ExceptionId}
                    AND outgoing_revoke_event IS NULL AND state IN ('AwaitingConsents', 'ReadyForReview')
                """, ct)
            : await db.Database.ExecuteSqlAsync($"""
                UPDATE public.coverage_replacement_exceptions
                SET incoming_revoke_event = {c.EventId}, state = 'Blocked', reason = 'ConsentRevoked'
                WHERE account_id = {row.AccountId} AND exception_id = {row.ExceptionId}
                    AND incoming_revoke_event IS NULL AND state IN ('AwaitingConsents', 'ReadyForReview')
                """, ct);
        return updated == 1;
    }

    private async Task<bool> AbortPreparingForRevocationAsync(ExceptionConsentRow row, CoverageReplacementExceptionConsentV1 c,
        bool isOutgoing, DateTimeOffset now, CancellationToken ct)
    {
        var prep = await db.Database.SqlQuery<PrepareMsg>($"""
            SELECT a.approval_id, m.payload FROM public.coverage_replacement_exception_approvals a
            JOIN public.entitlement_messages m ON m.account_id = a.account_id AND m.event_id = a.prepare_event
            WHERE a.account_id = {row.AccountId} AND a.exception_id = {row.ExceptionId} AND a.state = 'Preparing'
            """).SingleOrDefaultAsync(ct);
        if (prep is null) return true;

        var cmd = JsonSerializer.Deserialize<CoverageExceptionCommandV1>(prep.Payload, EntitlementSnapshotsV1.Json)!;
        var abortEventId = await QueueCommandAsync(cmd with
        {
            EventId = Guid.NewGuid(),
            Phase = CoverageReplacementExceptionsV1.AbortKind,
            PrepareDigest = null,
            ExpectedMerchantPermitDigest = null,
        }, ct);
        await db.Database.ExecuteSqlAsync($"""
            UPDATE public.coverage_replacement_exception_approvals
            SET state = 'Aborting', outcome_reason = 'ConsentRevoked', abort_event = {abortEventId}
            WHERE account_id = {row.AccountId} AND approval_id = {prep.ApprovalId} AND state = 'Preparing'
            """, ct);
        var updated = isOutgoing
            ? await db.Database.ExecuteSqlAsync($"""
                UPDATE public.coverage_replacement_exceptions
                SET outgoing_revoke_event = {c.EventId}, state = 'Aborting', reason = 'ConsentRevoked'
                WHERE account_id = {row.AccountId} AND exception_id = {row.ExceptionId} AND state = 'Preparing'
                """, ct)
            : await db.Database.ExecuteSqlAsync($"""
                UPDATE public.coverage_replacement_exceptions
                SET incoming_revoke_event = {c.EventId}, state = 'Aborting', reason = 'ConsentRevoked'
                WHERE account_id = {row.AccountId} AND exception_id = {row.ExceptionId} AND state = 'Preparing'
                """, ct);
        return updated == 1 && now <= row.NormalRenewalAt;
    }

    private async Task<bool> ApplyAcceptanceAsync(ExceptionConsentRow row, CoverageReplacementExceptionConsentV1 c,
        bool isOutgoing, DateTimeOffset now, CancellationToken ct)
    {
        if (row.State is not ("AwaitingConsents" or "ReadyForReview")) return false;
        if (!isOutgoing && c.ActiveMembers > row.MemberAllowance) return false;
        if (isOutgoing ? row.OutgoingConsentEvent is not null || row.OutgoingRevokeEvent is not null
            : row.IncomingConsentEvent is not null || row.IncomingRevokeEvent is not null)
            return false;

        var bothConsented = isOutgoing ? row.IncomingConsentEvent is not null : row.OutgoingConsentEvent is not null;
        var nextState = "AwaitingConsents";
        string? blockReason = null;
        if (bothConsented)
        {
            var source = await terms.SourceAsync(row.AccountId, row.TermId, now, ct);
            var stale = await db.Database.SqlQuery<bool>($"""
                SELECT public.coverage_exception_stale({row.ExceptionId}, {now}) AS "Value"
                """).SingleAsync(ct);
            var valid = !stale && source is { Closed: false, Superseded: false, Suspended: false }
                && source.Hash() == row.SourceHash;
            nextState = valid ? "ReadyForReview" : "Blocked";
            blockReason = valid ? null : "SourceChanged";
        }

        var updated = isOutgoing
            ? await db.Database.ExecuteSqlAsync($"""
                UPDATE public.coverage_replacement_exceptions
                SET outgoing_consent_event = {c.EventId}, outgoing_actor_id = {c.ActorId},
                    outgoing_authority_version = {c.AuthorityVersion}, outgoing_active_members = {c.ActiveMembers},
                    outgoing_seat_hash = {c.SeatHash}, outgoing_consented_at = {c.RecordedAt},
                    state = {nextState}, reason = {blockReason}::text
                WHERE account_id = {row.AccountId} AND exception_id = {row.ExceptionId}
                    AND outgoing_consent_event IS NULL AND state IN ('AwaitingConsents', 'ReadyForReview')
                """, ct)
            : await db.Database.ExecuteSqlAsync($"""
                UPDATE public.coverage_replacement_exceptions
                SET incoming_consent_event = {c.EventId}, incoming_actor_id = {c.ActorId},
                    incoming_authority_version = {c.AuthorityVersion}, incoming_active_members = {c.ActiveMembers},
                    incoming_seat_hash = {c.SeatHash}, incoming_consented_at = {c.RecordedAt},
                    state = {nextState}, reason = {blockReason}::text
                WHERE account_id = {row.AccountId} AND exception_id = {row.ExceptionId}
                    AND incoming_consent_event IS NULL AND state IN ('AwaitingConsents', 'ReadyForReview')
                """, ct);
        return updated == 1;
    }

    private async Task<Guid> QueueCommandAsync(CoverageExceptionCommandV1 command, CancellationToken ct)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(command, EntitlementSnapshotsV1.Json);
        await new LeasedHttpOutbox(db).AppendAsync(command.EventId, command.AccountId, command.OutgoingShopId,
            command.Phase, payload, SignedServiceAssertion.Digest(payload),
            EntitlementSnapshotsV1.Instant(deliveryClock), ct);
        return command.EventId;
    }

    private sealed record ExceptionConsentRow(
        Guid ExceptionId, Guid AccountId, Guid TermId, long ProposalRevision, string TermsDigest,
        Guid OutgoingShopId, Guid OutgoingAffiliationId, Guid IncomingShopId, Guid IncomingAffiliationId,
        int MemberAllowance, DateTimeOffset RequestedEarlyStartAt, DateTimeOffset PaidCutoverAt,
        DateTimeOffset NormalRenewalAt, string OverlapDisposition, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
        string SourceHash, string State, Guid? OutgoingConsentEvent, Guid? OutgoingRevokeEvent,
        Guid? IncomingConsentEvent, Guid? IncomingRevokeEvent);

    private sealed record PrepareMsg(Guid ApprovalId, byte[] Payload);
}
