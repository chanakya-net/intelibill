using System.Text.Json;
using Intelibill.Licensing.Application.Grants;
using Intelibill.Licensing.Application.Subscriptions;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Licensing.Infrastructure.Grants;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Infrastructure.Entitlements;

internal sealed class CoverageExceptionAcknowledgements(
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
        if (row is null || c.RecordedAt < row.CreatedAt || c.RecordedAt > now || (c.Kind != "Revoked" && c.RecordedAt >= row.ExpiresAt))
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

    internal async Task<bool> ApplyReceiptAsync(CoverageExceptionReceiptV1 ack, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<ApprovalContext>($"""
            SELECT a.approval_id, a.exception_id, a.account_id, a.term_id, a.generation, a.admin_id, a.session_id,
                a.operation_key, a.payload_digest, a.expected_source_hash, a.expected_grant_version, a.disposition,
                a.reason AS approval_reason, a.prepare_expires_at, a.state AS approval_state,
                e.outgoing_shop_id, e.outgoing_shop_name, e.outgoing_affiliation_id,
                e.incoming_shop_id, e.incoming_shop_name, e.incoming_affiliation_id,
                e.member_allowance, e.requested_early_start_at, e.paid_cutover_at, e.normal_renewal_at,
                e.observed_permit_deadline, e.expires_at, e.source_hash, e.state AS exception_state,
                e.outgoing_revoke_event, e.incoming_revoke_event,
                m.payload, m.digest
            FROM public.coverage_replacement_exception_approvals a
            JOIN public.coverage_replacement_exceptions e USING (exception_id, account_id)
            JOIN public.entitlement_messages m ON m.account_id = a.account_id AND m.event_id = {ack.EventId}
            WHERE a.account_id = {ack.AccountId} AND a.exception_id = {ack.ExceptionId}
                AND a.approval_id = {ack.ApprovalId} AND a.term_id = {ack.TermId} AND a.generation = {ack.Generation}
                AND m.kind = {ack.Phase} AND m.shop_id = {ack.RoutingShopId}
                AND (a.prepare_event = {ack.EventId} OR a.consume_event = {ack.EventId} OR a.abort_event = {ack.EventId})
            """).SingleOrDefaultAsync(ct);
        if (row is null || row.Digest != ack.CommandDigest)
            return false;

        var command = JsonSerializer.Deserialize<CoverageExceptionCommandV1>(row.Payload, EntitlementSnapshotsV1.Json)!;
        var now = EntitlementSnapshotsV1.Instant(businessClock);
        if (ack.Phase == CoverageReplacementExceptionsV1.PrepareKind)
            await HandlePrepareReceiptAsync(row, command, ack, now, ct);
        else if (ack.Phase == CoverageReplacementExceptionsV1.AbortKind)
            await HandleAbortReceiptAsync(row, ack, ct);
        else if (ack.Phase == CoverageReplacementExceptionsV1.ConsumeKind)
            await HandleConsumeReceiptAsync(row, ack, now, ct);

        return await db.Database.ExecuteSqlAsync($"""
            UPDATE public.entitlement_outbox
            SET status = {(ack.Outcome == "Refused" ? "NeedsReview" : "Applied")},
                lease_token = NULL, lease_expires_at = NULL, last_failure = NULL
            WHERE event_id = {ack.EventId} AND account_id = {ack.AccountId}
            """, ct) == 1;
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

    private async Task HandlePrepareReceiptAsync(ApprovalContext row, CoverageExceptionCommandV1 command,
        CoverageExceptionReceiptV1 ack, DateTimeOffset now, CancellationToken ct)
    {
        if (row.ApprovalState != "Preparing") return;
        if (ack.Outcome == "Refused")
        {
            await MarkApprovalAndExceptionRefusedAsync(row, ack, "Refused", "Blocked", ack.Reason ?? "MerchantRefused", ct);
            return;
        }

        var abortReason = await CheckPreparedStillValidAsync(row, ack, now, ct);
        if (abortReason is not null)
        {
            var abortId = await QueueCommandAsync(command with
            {
                EventId = Guid.NewGuid(),
                Phase = CoverageReplacementExceptionsV1.AbortKind,
                PrepareDigest = null,
                ExpectedMerchantPermitDigest = null,
            }, ct);
            await db.Database.ExecuteSqlAsync($"""
                UPDATE public.coverage_replacement_exception_approvals
                SET state = 'Aborting', outcome_reason = {abortReason}, abort_event = {abortId},
                    merchant_permit_count = {ack.OutgoingPermitCount}, merchant_max_permit_end = {ack.MaxPermitValidUntil}::timestamptz,
                    merchant_permit_digest = {ack.PermitEvidenceDigest}
                WHERE account_id = {row.AccountId} AND approval_id = {row.ApprovalId} AND state = 'Preparing'
                """, ct);
            await db.Database.ExecuteSqlAsync($"""
                UPDATE public.coverage_replacement_exceptions
                SET state = 'Aborting', reason = {abortReason}
                WHERE account_id = {row.AccountId} AND exception_id = {row.ExceptionId} AND state = 'Preparing'
                """, ct);
            return;
        }

        var consumeId = await QueueCommandAsync(command with
        {
            EventId = Guid.NewGuid(),
            Phase = CoverageReplacementExceptionsV1.ConsumeKind,
            PrepareDigest = row.Digest,
            ExpectedMerchantPermitDigest = ack.PermitEvidenceDigest,
        }, ct);
        await db.Database.ExecuteSqlAsync($"""
            UPDATE public.coverage_replacement_exception_approvals
            SET state = 'Consuming', consume_event = {consumeId},
                merchant_permit_count = {ack.OutgoingPermitCount}, merchant_max_permit_end = {ack.MaxPermitValidUntil}::timestamptz,
                merchant_permit_digest = {ack.PermitEvidenceDigest}
            WHERE account_id = {row.AccountId} AND approval_id = {row.ApprovalId} AND state = 'Preparing'
            """, ct);
        await db.Database.ExecuteSqlAsync($"""
            UPDATE public.coverage_replacement_exceptions SET state = 'Consuming'
            WHERE account_id = {row.AccountId} AND exception_id = {row.ExceptionId} AND state = 'Preparing'
            """, ct);
    }

    private async Task<string?> CheckPreparedStillValidAsync(ApprovalContext row, CoverageExceptionReceiptV1 ack,
        DateTimeOffset now, CancellationToken ct)
    {
        if (row.ExceptionState != "Preparing") return "StateChanged";
        if (row.OutgoingRevokeEvent is not null || row.IncomingRevokeEvent is not null) return "ConsentRevoked";
        if (now >= row.PrepareExpiresAt || now >= row.ExpiresAt || now >= row.NormalRenewalAt) return "PrepareExpired";
        if (ack.MaxPermitValidUntil is { } mEnd && (mEnd > row.PaidCutoverAt
            || row.ObservedPermitDeadline is null || mEnd > row.ObservedPermitDeadline))
            return "UnseenPermitEvidence";

        var source = await terms.SourceAsync(row.AccountId, row.TermId, now, ct);
        if (source is not { Closed: false, Superseded: false, Suspended: false } || source.Hash() != row.SourceHash)
            return "SourceChanged";
        if (!source.Shops.Any(s => s.ShopId == row.OutgoingShopId && s.RequestId == row.OutgoingAffiliationId))
            return "OutgoingChanged";

        var live = await db.Database.SqlQuery<LivePrepCheck>($"""
            SELECT public.coverage_exception_owner_current({row.AccountId}, {row.ExceptionId}) AS owner_current,
                coalesce((SELECT revision FROM public.complimentary_grant_scopes WHERE account_id = {row.AccountId}), 0) AS grant_version,
                public.replacement_permit_end({row.AccountId}, {row.OutgoingShopId}, {row.OutgoingAffiliationId}, {now}) AS live_permit_end,
                EXISTS (SELECT FROM public.shop_affiliations a WHERE a.account_id = {row.AccountId}
                    AND a.shop_id = {row.IncomingShopId} AND a.request_id = {row.IncomingAffiliationId}) AS incoming_affiliated,
                EXISTS (SELECT FROM public.active_operational_closure_targets c WHERE c.account_id = {row.AccountId}
                    AND c.shop_id IN ({row.OutgoingShopId}, {row.IncomingShopId})) AS any_closed,
                EXISTS (SELECT FROM public.active_operational_restrictions r WHERE r.account_id = {row.AccountId}
                    AND r.effective_at <= {now} AND (r.shop_id IS NULL OR r.shop_id IN ({row.OutgoingShopId}, {row.IncomingShopId}))) AS any_suspended,
                EXISTS (SELECT FROM public.complimentary_grant_shops g WHERE g.shop_id = {row.IncomingShopId}
                    AND g.starts_at < {row.PaidCutoverAt} AND {row.RequestedEarlyStartAt} < g.ends_at) AS grant_overlap
            """).SingleAsync(ct);

        if (!live.OwnerCurrent) return "BillingOwnerChanged";
        if (live.GrantVersion != row.ExpectedGrantVersion) return "GrantVersionChanged";
        if (!live.IncomingAffiliated || live.AnyClosed || live.AnySuspended) return "SourceChanged";
        if (row.Disposition == CoverageReplacementExceptionsV1.CourtesyBridgeDisposition && live.GrantOverlap)
            return "GrantOverlap";
        if (live.LivePermitEnd is { } obsEnd && (obsEnd > row.PaidCutoverAt
            || row.ObservedPermitDeadline is null || obsEnd > row.ObservedPermitDeadline))
            return "PermitEvidenceChanged";
        return null;
    }

    private async Task HandleAbortReceiptAsync(ApprovalContext row, CoverageExceptionReceiptV1 ack, CancellationToken ct)
    {
        if (row.ApprovalState != "Aborting") return;
        var approvalState = ack.Outcome == "Aborted" ? "Aborted" : "Refused";
        var reason = ack.Reason ?? "Aborted";
        await db.Database.ExecuteSqlAsync($"""
            UPDATE public.coverage_replacement_exception_approvals
            SET state = {approvalState}, outcome_reason = coalesce(outcome_reason, {reason})
            WHERE account_id = {row.AccountId} AND approval_id = {row.ApprovalId} AND state = 'Aborting'
            """, ct);
        await db.Database.ExecuteSqlAsync($"""
            UPDATE public.coverage_replacement_exceptions
            SET state = 'Blocked', reason = coalesce(reason, {reason})
            WHERE account_id = {row.AccountId} AND exception_id = {row.ExceptionId} AND state = 'Aborting'
            """, ct);
    }

    private async Task HandleConsumeReceiptAsync(ApprovalContext row, CoverageExceptionReceiptV1 ack,
        DateTimeOffset now, CancellationToken ct)
    {
        if (row.ApprovalState != "Consuming") return;
        if (ack.Outcome == "Refused")
        {
            await MarkApprovalAndExceptionRefusedAsync(row, ack, "Refused", "Blocked", ack.Reason ?? "ConsumeRefused", ct);
            return;
        }

        await db.Database.ExecuteSqlAsync($"""
            SELECT set_config('app.coverage_exception_applying_id', {row.ExceptionId.ToString("D")}, true)
            """, ct);

        Guid? courtesyGrantId = null;
        Guid? courtesyTermId = null;
        DateTimeOffset? courtesyStartsAt = null;
        if (row.Disposition == CoverageReplacementExceptionsV1.CourtesyBridgeDisposition)
        {
            (courtesyGrantId, courtesyTermId) = await CreateLinkedCourtesyGrantAsync(row, now, ct);
            courtesyStartsAt = row.RequestedEarlyStartAt;
        }

        await db.Database.ExecuteSqlAsync($"""
            UPDATE public.coverage_replacement_exception_approvals
            SET state = 'Applied', consume_receipt_event = {ack.EventId},
                courtesy_grant_id = {courtesyGrantId}::uuid, courtesy_term_id = {courtesyTermId}::uuid,
                applied_at = {now},
                merchant_permit_count = {ack.OutgoingPermitCount},
                merchant_max_permit_end = {ack.MaxPermitValidUntil}::timestamptz,
                merchant_permit_digest = {ack.PermitEvidenceDigest}
            WHERE account_id = {row.AccountId} AND approval_id = {row.ApprovalId} AND state = 'Consuming'
            """, ct);

        await db.Database.ExecuteSqlAsync($"""
            UPDATE public.coverage_replacement_exceptions
            SET state = 'Applied', applied_at = {now}
            WHERE account_id = {row.AccountId} AND exception_id = {row.ExceptionId} AND state = 'Consuming'
            """, ct);

        var slot = await db.Database.SqlQuery<SlotLineage>($"""
            SELECT i.slot_origin_shop_id, i.interval_starts_at AS previous_starts_at,
                (SELECT count(*)::int + 1 FROM public.paid_slot_handoffs h
                 WHERE h.account_id = {row.AccountId} AND h.term_id = {row.TermId}
                     AND h.slot_origin_shop_id = i.slot_origin_shop_id) AS slot_generation
            FROM public.term_paid_slot_intervals({row.AccountId}, {row.TermId}) i
            WHERE i.shop_id = {row.OutgoingShopId} AND i.affiliation_request_id = {row.OutgoingAffiliationId}
                AND i.is_final_holder
            """).SingleAsync(ct);

        var handoffId = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.paid_slot_handoffs (
                handoff_id, exception_id, approval_id, generation, account_id, term_id,
                slot_origin_shop_id, slot_generation,
                outgoing_shop_id, outgoing_affiliation_id, incoming_shop_id, incoming_affiliation_id,
                previous_starts_at, cutover_at, original_ends_at,
                courtesy_grant_id, courtesy_term_id, courtesy_starts_at, consume_receipt_event, recorded_at)
            VALUES ({handoffId}, {row.ExceptionId}, {row.ApprovalId}, {row.Generation}, {row.AccountId}, {row.TermId},
                {slot.SlotOriginShopId}, {slot.SlotGeneration},
                {row.OutgoingShopId}, {row.OutgoingAffiliationId}, {row.IncomingShopId}, {row.IncomingAffiliationId},
                {slot.PreviousStartsAt}, {row.PaidCutoverAt}, {row.NormalRenewalAt},
                {courtesyGrantId}::uuid, {courtesyTermId}::uuid, {courtesyStartsAt}::timestamptz, {ack.EventId}, {now})
            """, ct);

        await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE", ct);
        foreach (var shopId in new[] { row.OutgoingShopId, row.IncomingShopId }.Order())
            await publisher.PublishAsync(row.AccountId, shopId, ct);
    }

    private async Task<(Guid GrantId, Guid TermId)> CreateLinkedCourtesyGrantAsync(ApprovalContext row,
        DateTimeOffset now, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"""
            SELECT set_config('app.grant_admin_id', {row.AdminId.ToString("D")}, true)
            """, ct);
        var currentVersion = await grants.ReadRevisionAsync(row.AccountId, ct);
        var grantId = Guid.NewGuid();
        var termId = Guid.NewGuid();
        var reasonText = $"Early replacement courtesy bridge ({row.ExceptionId:D}): {row.ApprovalReason}";
        if (reasonText.Length > ComplimentaryAccessPolicy.MaximumReasonLength)
            reasonText = reasonText[..ComplimentaryAccessPolicy.MaximumReasonLength];

        var facts = new ComplimentaryGrantFacts([row.IncomingShopId], row.RequestedEarlyStartAt, row.PaidCutoverAt,
            row.MemberAllowance, reasonText);
        var stored = new StoredComplimentaryGrant(grantId, termId, row.AccountId, row.ApprovalId, row.AdminId,
            row.SessionId, facts, currentVersion, row.ExpectedSourceHash, row.PayloadDigest, now,
            [new GrantShopSource(row.IncomingShopId, row.IncomingShopName, row.IncomingAffiliationId)]);
        await grants.SaveCoreAsync(stored, ct);
        return (grantId, termId);
    }

    private async Task MarkApprovalAndExceptionRefusedAsync(ApprovalContext row, CoverageExceptionReceiptV1 ack,
        string approvalState, string exceptionState, string reason, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"""
            UPDATE public.coverage_replacement_exception_approvals
            SET state = {approvalState}, outcome_reason = {reason},
                merchant_permit_count = {ack.OutgoingPermitCount},
                merchant_max_permit_end = {ack.MaxPermitValidUntil}::timestamptz,
                merchant_permit_digest = {ack.PermitEvidenceDigest}
            WHERE account_id = {row.AccountId} AND approval_id = {row.ApprovalId} AND state = {row.ApprovalState}
            """, ct);
        await db.Database.ExecuteSqlAsync($"""
            UPDATE public.coverage_replacement_exceptions
            SET state = {exceptionState}, reason = {reason}
            WHERE account_id = {row.AccountId} AND exception_id = {row.ExceptionId} AND state = {row.ExceptionState}
            """, ct);
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

    private sealed record ApprovalContext(
        Guid ApprovalId, Guid ExceptionId, Guid AccountId, Guid TermId, long Generation, Guid AdminId, Guid SessionId,
        Guid OperationKey, string PayloadDigest, string ExpectedSourceHash, long ExpectedGrantVersion,
        string Disposition, string ApprovalReason, DateTimeOffset PrepareExpiresAt, string ApprovalState,
        Guid OutgoingShopId, string OutgoingShopName, Guid OutgoingAffiliationId,
        Guid IncomingShopId, string IncomingShopName, Guid IncomingAffiliationId,
        int MemberAllowance, DateTimeOffset RequestedEarlyStartAt, DateTimeOffset PaidCutoverAt,
        DateTimeOffset NormalRenewalAt, DateTimeOffset? ObservedPermitDeadline, DateTimeOffset ExpiresAt,
        string SourceHash, string ExceptionState, Guid? OutgoingRevokeEvent, Guid? IncomingRevokeEvent,
        byte[] Payload, string Digest);

    private sealed record LivePrepCheck(bool OwnerCurrent, long GrantVersion, DateTimeOffset? LivePermitEnd,
        bool IncomingAffiliated, bool AnyClosed, bool AnySuspended, bool GrantOverlap);

    private sealed record SlotLineage(Guid SlotOriginShopId, DateTimeOffset PreviousStartsAt, int SlotGeneration);
}
