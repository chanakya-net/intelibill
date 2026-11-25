using System.Data;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Licensing;
using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Infrastructure.Licensing;

internal sealed class MerchantCoverageExceptionReceiver(
    ApplicationDbContext db,
    EntitlementProjectionStore projection,
    ShopLicenceFence fence,
    TimeProvider transportClock,
    [FromKeyedServices(ShopTrialsV1.DeliverySection)] TimeProvider businessClock)
    : ICoverageExceptionReceiver
{
    public async Task<ErrorOr<Success>> ReceiveAsync(CoverageExceptionCommandV1 c, byte[] body,
        VerifiedServiceAssertion proof, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await db.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {ShopLicenceFence.LockTimeout}, true)", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('app.active_shop_id', {c.OutgoingShopId.ToString("D")}, true),
                set_config('app.entitlement_account_id', {c.AccountId.ToString("D")}, true)
            """, ct);

        var entered = await fence.EnterDualProjectionAsync(c.AccountId, c.OutgoingShopId, c.IncomingShopId, ct);
        if (entered.IsError) return entered.Errors;
        if (!await projection.ConsumeAsync(proof, ct))
            return Error.Unauthorized("Coverage.ExceptionReplayed");

        var prior = await PriorAsync(c.ExceptionId, c.Generation, c.Phase, ct);
        if (prior is not null)
        {
            if (prior.EventId != c.EventId || prior.Digest != proof.Digest)
                return Error.Conflict("Coverage.ExceptionCommandMismatch");
            await db.Database.ExecuteSqlAsync($"""
                UPDATE public.entitlement_outbox
                SET status = 'Pending', attempts = 0, next_attempt_at = {transportClock.GetUtcNow()}, last_failure = NULL
                WHERE account_id = {c.AccountId} AND shop_id = {c.OutgoingShopId}
                    AND event_id = {prior.EventId} AND status = 'DeadLetter'
                """, ct);
        }
        else if (!await DecideAsync(c, body, proof, ct))
        {
            return Error.Conflict("Coverage.ExceptionCommandMismatch");
        }

        if (transportClock.GetUtcNow() >= proof.ExpiresAt)
            return Error.Unauthorized("Coverage.ExceptionReplayed");
        await tx.CommitAsync(ct);
        return Result.Success;
    }

    private async Task<bool> DecideAsync(CoverageExceptionCommandV1 c, byte[] body,
        VerifiedServiceAssertion proof, CancellationToken ct)
    {
        var now = EntitlementSnapshotsV1.Instant(businessClock);
        var inspect = await InspectFenceAsync(c, now, ct);
        if (inspect is null) return false;

        var (outcome, reason) = await EvaluateAsync(c, inspect, now, ct);
        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.merchant_coverage_exception_decisions (
                event_id, exception_id, approval_id, generation, phase, account_id, term_id,
                outgoing_shop_id, outgoing_affiliation_id, incoming_shop_id, incoming_affiliation_id,
                requested_early_start_at, paid_cutover_at, normal_renewal_at, overlap_disposition,
                member_allowance, expires_at, digest, permit_count, max_permit_valid_until,
                permit_digest, fence_hash, outcome, reason, command, assertion, public_key, recorded_at)
            VALUES ({c.EventId}, {c.ExceptionId}, {c.ApprovalId}, {c.Generation}, {c.Phase}, {c.AccountId}, {c.TermId},
                {c.OutgoingShopId}, {c.OutgoingAffiliationId}, {c.IncomingShopId}, {c.IncomingAffiliationId},
                {c.RequestedEarlyStartAt}, {c.PaidCutoverAt}, {c.NormalRenewalAt}, {c.OverlapDisposition},
                {c.MemberAllowance}, {c.ExpiresAt}, {proof.Digest}, {inspect.OutgoingPermitCount},
                {inspect.MaxPermitValidUntil}::timestamptz, {inspect.PermitDigest}, {inspect.FenceHash},
                {outcome}, {reason}::text, {body}, {proof.Assertion}, {proof.PublicKey}, {now})
            ON CONFLICT DO NOTHING
            """, ct);
        if (inserted != 1) return false;

        if (outcome == "Consumed")
        {
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO public.merchant_coverage_exception_cutoffs (
                    exception_id, generation, account_id, term_id, shop_id, affiliation_request_id,
                    cutoff_at, decision_event_id, recorded_at)
                VALUES ({c.ExceptionId}, {c.Generation}, {c.AccountId}, {c.TermId},
                    {c.OutgoingShopId}, {c.OutgoingAffiliationId}, {c.PaidCutoverAt}, {c.EventId}, {now})
                """, ct);
        }

        var receipt = new CoverageExceptionReceiptV1(
            CoverageReplacementExceptionsV1.SchemaVersion, c.EventId, c.Phase, c.ExceptionId, c.ApprovalId,
            c.Generation, c.AccountId, c.TermId, c.OutgoingShopId, proof.Digest, outcome, reason,
            inspect.OutgoingPermitCount, inspect.MaxPermitValidUntil, inspect.PermitDigest, now);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, EntitlementSnapshotsV1.Json);
        await new LeasedHttpOutbox(db).AppendAsync(c.EventId, c.AccountId, c.OutgoingShopId,
            CoverageReplacementExceptionsV1.ReceiptKind, bytes, SignedServiceAssertion.Digest(bytes),
            EntitlementSnapshotsV1.Instant(transportClock), ct);
        return true;
    }

    private async Task<(string Outcome, string? Reason)> EvaluateAsync(CoverageExceptionCommandV1 c,
        FenceInspection f, DateTimeOffset now, CancellationToken ct)
    {
        var alreadyConsumed = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.merchant_coverage_exception_decisions
                WHERE exception_id = {c.ExceptionId} AND outcome = 'Consumed') AS "Value"
            """).SingleAsync(ct);

        if (c.Phase == CoverageReplacementExceptionsV1.AbortKind)
            return alreadyConsumed ? ("Refused", "AlreadyConsumed") : ("Aborted", null);

        if (alreadyConsumed) return ("Refused", "AlreadyConsumed");
        if (now >= c.ExpiresAt || now >= c.NormalRenewalAt || businessClock.GetUtcNow() >= c.ExpiresAt)
            return ("Refused", "CommandExpired");
        if (!f.OutgoingBound || !f.IncomingBound) return ("Refused", "AffiliationChanged");
        if (!f.OutgoingConsentValid) return ("Refused", "OutgoingConsentInvalid");
        if (!f.IncomingConsentValid) return ("Refused", "IncomingConsentInvalid");
        if (f.IncomingActiveMembers < 1 || f.IncomingActiveMembers > c.MemberAllowance)
            return ("Refused", "MemberCapacityExceeded");
        if (f.MaxPermitValidUntil is { } maxEnd)
        {
            if (maxEnd > c.PaidCutoverAt) return ("Refused", "PermitExceedsCutover");
            if (c.ExpectedObservedPermitDeadline is null || maxEnd > c.ExpectedObservedPermitDeadline)
                return ("Refused", "UnseenPermitEvidence");
        }

        if (c.Phase == CoverageReplacementExceptionsV1.PrepareKind)
        {
            var busy = await HasConflictingPreparedAsync(c, now, ct);
            return busy ? ("Refused", "ExceptionBusy") : ("Prepared", null);
        }

        var aborted = await PriorAsync(c.ExceptionId, c.Generation, CoverageReplacementExceptionsV1.AbortKind, ct);
        if (aborted is not null) return ("Refused", "AlreadyAborted");

        var prepared = await PriorAsync(c.ExceptionId, c.Generation, CoverageReplacementExceptionsV1.PrepareKind, ct);
        if (prepared is not { Outcome: "Prepared" } || prepared.Digest != c.PrepareDigest
            || prepared.PermitDigest != c.ExpectedMerchantPermitDigest)
            return ("Refused", "PrepareMissingOrChanged");
        if (prepared.PermitDigest != f.PermitDigest)
            return ("Refused", "PermitEvidenceChanged");
        if (prepared.FenceHash != f.FenceHash)
            return ("Refused", "FenceChanged");

        var original = JsonSerializer.Deserialize<CoverageExceptionCommandV1>(prepared.Command, EntitlementSnapshotsV1.Json);
        var expected = c with
        {
            EventId = prepared.EventId,
            Phase = CoverageReplacementExceptionsV1.PrepareKind,
            PrepareDigest = null,
            ExpectedMerchantPermitDigest = null,
        };
        return original == expected ? ("Consumed", null) : ("Refused", "PrepareMissingOrChanged");
    }

    private Task<bool> HasConflictingPreparedAsync(CoverageExceptionCommandV1 c, DateTimeOffset now, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT FROM public.merchant_coverage_exception_decisions d
                WHERE d.account_id = {c.AccountId} AND d.phase = 'CoverageExceptionPrepare'
                    AND d.outcome = 'Prepared' AND d.expires_at > {now}
                    AND (d.term_id = {c.TermId} OR d.outgoing_shop_id IN ({c.OutgoingShopId}, {c.IncomingShopId})
                        OR d.incoming_shop_id IN ({c.OutgoingShopId}, {c.IncomingShopId}))
                    AND NOT (d.exception_id = {c.ExceptionId} AND d.generation = {c.Generation})
                    AND NOT EXISTS (SELECT FROM public.merchant_coverage_exception_decisions x
                        WHERE x.exception_id = d.exception_id AND x.generation = d.generation
                            AND x.phase IN ('CoverageExceptionConsume', 'CoverageExceptionAbort'))
            ) AS "Value"
            """).SingleAsync(ct);

    private Task<FenceInspection?> InspectFenceAsync(CoverageExceptionCommandV1 c, DateTimeOffset now, CancellationToken ct) =>
        db.Database.SqlQuery<FenceInspection>($"""
            SELECT outgoing_bound, incoming_bound, outgoing_consent_valid, incoming_consent_valid,
                incoming_active_members, outgoing_permit_count, max_permit_valid_until, permit_digest, fence_hash
            FROM public.exception_fence_inspect(
                {c.AccountId}, {c.TermId}, {c.ExceptionId}, {c.ProposalRevision}, {c.TermsDigest},
                {c.OutgoingShopId}, {c.OutgoingAffiliationId}, {c.IncomingShopId}, {c.IncomingAffiliationId},
                {c.ExpectedOutgoingConsentEvent}, {c.ExpectedIncomingConsentEvent}, {c.MemberAllowance}, {now})
            """).SingleOrDefaultAsync(ct);

    private Task<PriorDecision?> PriorAsync(Guid exceptionId, long generation, string phase, CancellationToken ct) =>
        db.Database.SqlQuery<PriorDecision>($"""
            SELECT event_id, digest, permit_digest, fence_hash, outcome, command
            FROM public.merchant_coverage_exception_decisions
            WHERE exception_id = {exceptionId} AND generation = {generation} AND phase = {phase}
            """).SingleOrDefaultAsync(ct);

    private sealed record FenceInspection(bool OutgoingBound, bool IncomingBound, bool OutgoingConsentValid,
        bool IncomingConsentValid, int IncomingActiveMembers, int OutgoingPermitCount,
        DateTimeOffset? MaxPermitValidUntil, string PermitDigest, string FenceHash);

    private sealed record PriorDecision(Guid EventId, string Digest, string PermitDigest, string FenceHash,
        string Outcome, byte[] Command);
}
