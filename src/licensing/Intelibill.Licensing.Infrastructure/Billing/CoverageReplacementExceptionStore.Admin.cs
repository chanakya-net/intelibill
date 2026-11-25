using System.Data;
using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Changes;
using Intelibill.Licensing.Application.Coverage;
using Intelibill.Licensing.Application.Subscriptions;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.AdminIdentities;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal sealed partial class CoverageReplacementExceptionStore
{
    public async Task<ErrorOr<T>> InAdminTransactionAsync<T>(Func<CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'", ct);
            var result = await operation(ct);
            if (!result.IsError) await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception error) when (SqlState(error) is PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.DeadlockDetected)
        {
            return CoverageReplacementExceptionErrors.Busy;
        }
        catch (Exception error) when (SqlState(error) is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation)
        {
            return CoverageReplacementExceptionErrors.Changed;
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException or InvalidOperationException { InnerException: NpgsqlException })
        {
            ct.ThrowIfCancellationRequested();
            LogUnavailable(logger, SqlState(error) ?? error.GetType().Name);
            return CoverageReplacementExceptionErrors.Unavailable;
        }
    }

    public async Task<(AdminIdentity? Actor, AdminSession? Session)> LockProviderAsync(AdminSessionCaller caller, CancellationToken ct)
    {
        var actors = await db.Set<AdminIdentity>().FromSql($"""
            SELECT *, xmin FROM public.admin_identities WHERE id = {caller.AdminId} FOR SHARE
            """).AsNoTracking().ToListAsync(ct);
        var session = await AdminAuthorityLocks.LockSessionAsync(db, caller, ct);
        return (actors.SingleOrDefault(), session);
    }

    public Task EnterAdminAsync(Guid adminId, Guid sessionId, Guid account, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('app.exception_admin_id', {adminId.ToString("D")}, true),
                set_config('app.exception_session_id', {sessionId.ToString("D")}, true),
                set_config('app.grant_admin_id', {adminId.ToString("D")}, true),
                set_config('app.billing_account_id', {account.ToString("D")}, true)
            """, ct);

    public Task LockAccountAsync(Guid account, CancellationToken ct) => BillingAccountLock.TakeAsync(db, account, ct);

    public async Task<AdminCoverageExceptionListV1> ListAdminAsync(Guid account, DateTimeOffset now, CancellationToken ct)
    {
        var items = await db.Database.SqlQuery<AdminCoverageExceptionSummaryV1>($"""
            SELECT e.exception_id, e.account_id, e.term_id, e.proposal_revision,
                e.outgoing_shop_id, e.outgoing_shop_name, e.incoming_shop_id, e.incoming_shop_name,
                e.normal_renewal_at, e.requested_early_start_at, e.paid_cutover_at,
                e.overlap_disposition, e.observed_permit_deadline,
                CASE WHEN public.coverage_exception_stale(e.exception_id, {now}) THEN 'Blocked' ELSE e.state END AS state,
                CASE WHEN e.outgoing_revoke_event IS NOT NULL THEN 'Revoked'
                    WHEN e.outgoing_consent_event IS NOT NULL THEN 'Accepted' ELSE 'AwaitingConsent' END AS outgoing_consent_state,
                CASE WHEN e.incoming_revoke_event IS NOT NULL THEN 'Revoked'
                    WHEN e.incoming_consent_event IS NOT NULL THEN 'Accepted' ELSE 'AwaitingConsent' END AS incoming_consent_state,
                public.coverage_exception_leg_activation(e.exception_id, 'Outgoing', {now}) AS outgoing_activation,
                public.coverage_exception_leg_activation(e.exception_id, 'IncomingPaid', {now}) AS incoming_activation,
                public.coverage_exception_leg_activation(e.exception_id, 'IncomingCourtesy', {now}) AS courtesy_activation,
                h.courtesy_grant_id, e.created_at
            FROM public.coverage_replacement_exceptions e
            LEFT JOIN public.paid_slot_handoffs h ON h.exception_id = e.exception_id AND h.account_id = e.account_id
            WHERE e.account_id = {account}
            ORDER BY e.created_at DESC, e.exception_id
            LIMIT {CoverageReplacementExceptionsV1.PageSize}
            """).ToArrayAsync(ct);
        return new(account, now, items);
    }

    public async Task<ErrorOr<AdminCoverageExceptionPreviewV1>> PreviewAdminAsync(Guid account, Guid exceptionId,
        DateTimeOffset now, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<ExceptionDetailRow>($"""
            SELECT e.exception_id, e.account_id, e.term_id, e.proposal_revision, e.terms_digest,
                e.outgoing_shop_id, e.outgoing_shop_name, e.outgoing_affiliation_id,
                e.incoming_shop_id, e.incoming_shop_name, e.incoming_affiliation_id,
                e.capacity, e.member_allowance, e.normal_renewal_at, e.requested_early_start_at, e.paid_cutover_at,
                e.overlap_disposition, e.observed_permit_deadline, e.observed_at,
                e.last_contact_at, e.last_synchronized_at, e.observed_permit_count,
                e.request_reason, e.expires_at, e.requester_id, e.requester_revision, e.state, e.reason,
                e.outgoing_consent_event, e.outgoing_revoke_event, e.outgoing_actor_id,
                e.outgoing_authority_version, e.outgoing_active_members, e.outgoing_seat_hash, e.outgoing_consented_at,
                e.incoming_consent_event, e.incoming_revoke_event, e.incoming_actor_id,
                e.incoming_authority_version, e.incoming_active_members, e.incoming_seat_hash, e.incoming_consented_at,
                public.coverage_exception_owner_current(e.account_id, e.exception_id) AS owner_current,
                public.coverage_exception_stale(e.exception_id, {now}) AS is_stale
            FROM public.coverage_replacement_exceptions e
            WHERE e.account_id = {account} AND e.exception_id = {exceptionId}
            """).SingleOrDefaultAsync(ct);
        if (row is null) return CoverageReplacementExceptionErrors.NotFound;

        var live = await ReadAdminLiveContextAsync(account, row, now, ct);
        var approval = await ReadLatestApprovalAsync(account, exceptionId, now, ct);
        var blocking = CollectBlockingReasons(row, live, approval, now);
        var sourceHash = BuildAdminSourceHash(row, live);
        var courtesy = row.OverlapDisposition == CoverageReplacementExceptionsV1.CourtesyBridgeDisposition
            && row.RequestedEarlyStartAt < row.PaidCutoverAt;
        var outStatus = ConsentLabel(row.OutgoingConsentEvent, row.OutgoingRevokeEvent);
        var inStatus = ConsentLabel(row.IncomingConsentEvent, row.IncomingRevokeEvent);

        return new AdminCoverageExceptionPreviewV1(
            row.ExceptionId, row.AccountId, row.TermId, row.ProposalRevision,
            sourceHash, row.TermsDigest, live.GrantScopeVersion,
            row.OutgoingShopId, row.OutgoingShopName, row.OutgoingAffiliationId,
            row.IncomingShopId, row.IncomingShopName, row.IncomingAffiliationId,
            row.Capacity, row.MemberAllowance,
            row.NormalRenewalAt, row.RequestedEarlyStartAt, row.PaidCutoverAt, row.OverlapDisposition,
            live.LivePermitDeadline ?? row.ObservedPermitDeadline, now,
            live.LastContactAt ?? row.LastContactAt, live.LastSynchronizedAt ?? row.LastSynchronizedAt,
            live.OutstandingPermitCount, true, CoverageReplacementExceptionsV1.DisconnectedQueuesNotice,
            row.RequesterId, row.RequesterRevision, row.OwnerCurrent, true,
            new(CoverageReplacementExceptionsV1.OutgoingLeg, row.OutgoingShopId, row.OutgoingShopName, outStatus,
                row.OutgoingConsentEvent, row.OutgoingActorId, row.OutgoingAuthorityVersion, row.OutgoingActiveMembers, row.OutgoingConsentedAt),
            new(CoverageReplacementExceptionsV1.IncomingLeg, row.IncomingShopId, row.IncomingShopName, inStatus,
                row.IncomingConsentEvent, row.IncomingActorId, row.IncomingAuthorityVersion, row.IncomingActiveMembers, row.IncomingConsentedAt),
            row.RequestReason, row.IsStale ? "Blocked" : row.State, row.Reason,
            blocking.Length == 0 && row.State == "ReadyForReview" && !row.IsStale, blocking,
            courtesy, courtesy ? row.RequestedEarlyStartAt : null, courtesy ? row.PaidCutoverAt : null, approval);
    }

    public async Task<StoredExceptionApproval?> FindApprovalByKeyAsync(Guid account, Guid operationKey, DateTimeOffset now,
        CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<ApprovalRow>($"""
            SELECT a.approval_id, a.exception_id, a.account_id, a.term_id, a.generation, a.operation_key,
                a.admin_id, a.payload_digest, a.disposition, a.reason, a.decided_at, a.state, a.outcome_reason,
                a.merchant_permit_count, a.merchant_max_permit_end, a.courtesy_grant_id, a.courtesy_term_id,
                CASE WHEN a.courtesy_grant_id IS NOT NULL THEN e.requested_early_start_at END AS courtesy_starts_at,
                CASE WHEN a.courtesy_grant_id IS NOT NULL THEN e.paid_cutover_at END AS courtesy_ends_at,
                e.paid_cutover_at, e.normal_renewal_at, a.applied_at,
                public.coverage_exception_leg_activation(e.exception_id, 'Outgoing', {now}) AS outgoing_activation,
                public.coverage_exception_leg_activation(e.exception_id, 'IncomingPaid', {now}) AS incoming_activation,
                public.coverage_exception_leg_activation(e.exception_id, 'IncomingCourtesy', {now}) AS courtesy_activation
            FROM public.coverage_replacement_exception_approvals a
            JOIN public.coverage_replacement_exceptions e USING (exception_id, account_id)
            WHERE a.account_id = {account} AND a.operation_key = {operationKey}
            """).SingleOrDefaultAsync(ct);
        return row is null ? null : new(row.ApprovalId, row.ExceptionId, row.AdminId, row.PayloadDigest, row.ToView());
    }

    public async Task<ErrorOr<AdminCoverageExceptionApprovalV1>> RecordApprovalAndQueuePrepareAsync(
        Guid account, Guid exceptionId, AdminSessionCaller caller, DateTimeOffset mfaVerifiedAt,
        DateTimeOffset authorizedAt, DateTimeOffset decidedAt, ApproveCoverageReplacementRequestV1 request,
        string normalizedReason, string payloadDigest, AdminCoverageExceptionPreviewV1 preview, CancellationToken ct)
    {
        var generation = await db.Database.SqlQuery<long>($"""
            SELECT coalesce(max(generation), 0) + 1 AS "Value"
            FROM public.coverage_replacement_exception_approvals
            WHERE account_id = {account} AND exception_id = {exceptionId}
            """).SingleAsync(ct);

        var approvalId = Guid.NewGuid();
        var prepareEventId = Guid.NewGuid();
        var expiresAt = decidedAt.AddMinutes(15) < preview.NormalRenewalAt ? decidedAt.AddMinutes(15) : preview.NormalRenewalAt;
        var prepare = new CoverageExceptionCommandV1(
            CoverageReplacementExceptionsV1.SchemaVersion, prepareEventId, CoverageReplacementExceptionsV1.PrepareKind,
            exceptionId, approvalId, generation, account, preview.TermId,
            preview.OutgoingShopId, preview.OutgoingAffiliationId, preview.IncomingShopId, preview.IncomingAffiliationId,
            preview.ProposalRevision, preview.TermsDigest, preview.RequestedEarlyStartAt, preview.PaidCutoverAt,
            preview.NormalRenewalAt, preview.OverlapDisposition, preview.MemberAllowance,
            preview.OutgoingConsent.EventId!.Value, preview.IncomingConsent.EventId!.Value,
            preview.ObservedPermitDeadline, expiresAt);
        var payload = JsonSerializer.SerializeToUtf8Bytes(prepare, EntitlementSnapshotsV1.Json);
        await new LeasedHttpOutbox(db).AppendAsync(prepareEventId, account, preview.OutgoingShopId,
            CoverageReplacementExceptionsV1.PrepareKind, payload, SignedServiceAssertion.Digest(payload),
            EntitlementSnapshotsV1.Instant(deliveryClock), ct);

        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.coverage_replacement_exception_approvals (
                approval_id, exception_id, account_id, term_id, generation, admin_id, session_id,
                mfa_verified_at, authorized_at, decided_at, operation_key, payload_digest,
                expected_proposal_revision, expected_source_hash, expected_terms_digest, expected_grant_version,
                disposition, reason, prepare_event, prepare_expires_at, state)
            VALUES ({approvalId}, {exceptionId}, {account}, {preview.TermId}, {generation}, {caller.AdminId}, {caller.SessionId},
                {mfaVerifiedAt}, {authorizedAt}, {decidedAt}, {request.OperationKey}, {payloadDigest},
                {request.ExpectedProposalRevision}, {request.ExpectedSourceHash}, {request.ExpectedTermsDigest},
                {request.ExpectedGrantVersion}, {request.Disposition}, {normalizedReason},
                {prepareEventId}, {expiresAt}, 'Preparing')
            """, ct);

        var updated = await db.Database.ExecuteSqlAsync($"""
            UPDATE public.coverage_replacement_exceptions
            SET state = 'Preparing', approved_at = {decidedAt}
            WHERE account_id = {account} AND exception_id = {exceptionId} AND state = 'ReadyForReview'
            """, ct);
        if (updated != 1) return CoverageReplacementExceptionErrors.Changed;

        return (await FindApprovalByKeyAsync(account, request.OperationKey, decidedAt, ct))!.View;
    }

    private async Task<AdminLiveContext> ReadAdminLiveContextAsync(Guid account, ExceptionDetailRow row,
        DateTimeOffset now, CancellationToken ct)
    {
        var source = await terms.SourceAsync(account, row.TermId, now, ct);
        var grantVersion = await db.Database.SqlQuery<long?>($"""
            SELECT revision AS "Value" FROM public.complimentary_grant_scopes WHERE account_id = {account}
            """).SingleOrDefaultAsync(ct) ?? 0;
        var heads = await db.Database.SqlQuery<HeadRevisions>($"""
            SELECT coalesce((SELECT revision FROM public.entitlement_account_heads WHERE account_id = {account}), 0) AS account_rev,
                coalesce((SELECT revision FROM public.entitlement_shop_heads WHERE account_id = {account} AND shop_id = {row.OutgoingShopId}), 0) AS out_rev,
                coalesce((SELECT revision FROM public.entitlement_shop_heads WHERE account_id = {account} AND shop_id = {row.IncomingShopId}), 0) AS in_rev
            """).SingleAsync(ct);
        var obs = (await ReadObservedPermitsAsync(account, [row.OutgoingShopId], now, ct))
            .GetValueOrDefault((row.OutgoingShopId, row.OutgoingAffiliationId));
        var incomingSuspended = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.active_operational_restrictions r
                WHERE r.account_id = {account} AND r.effective_at <= {now}
                    AND (r.shop_id IS NULL OR r.shop_id IN ({row.OutgoingShopId}, {row.IncomingShopId}))) AS "Value"
            """).SingleAsync(ct);
        return new(source, grantVersion, heads.AccountRev, heads.OutRev, heads.InRev,
            obs?.ValidUntil, obs?.LastContactAt, obs?.LastSynchronizedAt, obs?.OutstandingCount ?? 0, incomingSuspended);
    }

    private async Task<AdminCoverageExceptionApprovalV1?> ReadLatestApprovalAsync(Guid account, Guid exceptionId,
        DateTimeOffset now, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<ApprovalRow>($"""
            SELECT a.approval_id, a.exception_id, a.account_id, a.term_id, a.generation, a.operation_key,
                a.admin_id, a.payload_digest, a.disposition, a.reason, a.decided_at, a.state, a.outcome_reason,
                a.merchant_permit_count, a.merchant_max_permit_end, a.courtesy_grant_id, a.courtesy_term_id,
                CASE WHEN a.courtesy_grant_id IS NOT NULL THEN e.requested_early_start_at END AS courtesy_starts_at,
                CASE WHEN a.courtesy_grant_id IS NOT NULL THEN e.paid_cutover_at END AS courtesy_ends_at,
                e.paid_cutover_at, e.normal_renewal_at, a.applied_at,
                public.coverage_exception_leg_activation(e.exception_id, 'Outgoing', {now}) AS outgoing_activation,
                public.coverage_exception_leg_activation(e.exception_id, 'IncomingPaid', {now}) AS incoming_activation,
                public.coverage_exception_leg_activation(e.exception_id, 'IncomingCourtesy', {now}) AS courtesy_activation
            FROM public.coverage_replacement_exception_approvals a
            JOIN public.coverage_replacement_exceptions e USING (exception_id, account_id)
            WHERE a.account_id = {account} AND a.exception_id = {exceptionId}
            ORDER BY a.generation DESC LIMIT 1
            """).SingleOrDefaultAsync(ct);
        return row?.ToView();
    }

    private static string[] CollectBlockingReasons(ExceptionDetailRow row, AdminLiveContext live,
        AdminCoverageExceptionApprovalV1? approval, DateTimeOffset now)
    {
        var reasons = new List<string>();
        if (!row.OwnerCurrent) reasons.Add("BillingOwnerChanged");
        if (row.OutgoingRevokeEvent is not null) reasons.Add("OutgoingConsentRevoked");
        else if (row.OutgoingConsentEvent is null) reasons.Add("OutgoingConsentMissing");
        if (row.IncomingRevokeEvent is not null) reasons.Add("IncomingConsentRevoked");
        else if (row.IncomingConsentEvent is null) reasons.Add("IncomingConsentMissing");
        if (row.IncomingActiveMembers > row.MemberAllowance) reasons.Add("MemberCapacityExceeded");
        if (now >= row.ExpiresAt || now >= row.NormalRenewalAt) reasons.Add("ProposalExpired");
        if (live.Source is null || ScheduledChangeValidator.ValidateSource(live.Source, now, row.Capacity, row.MemberAllowance, row.NormalRenewalAt) is not null)
            reasons.Add("TermUnavailable");
        if (live.AnySuspended) reasons.Add("ShopSuspended");
        if (live.LivePermitDeadline is { } seen && (seen > row.PaidCutoverAt || row.ObservedPermitDeadline is null || seen > row.ObservedPermitDeadline))
            reasons.Add("PermitEvidenceChanged");
        if (row.IsStale) reasons.Add("ProposalStale");
        if (approval is { State: "Preparing" or "Consuming" or "Applied" or "Aborting" }) reasons.Add("ApprovalAlreadyRecorded");
        return [.. reasons];
    }

    private static string BuildAdminSourceHash(ExceptionDetailRow r, AdminLiveContext live) =>
        CheckoutOrdersV1.Digest("coverage-exception-admin-source/1", new
        {
            termSource = live.Source?.Hash() ?? "missing",
            r.TermsDigest,
            r.ProposalRevision,
            r.RequesterId,
            r.RequesterRevision,
            r.OwnerCurrent,
            live.GrantScopeVersion,
            live.AccountRev,
            live.OutRev,
            live.InRev,
            live.LivePermitDeadline,
            live.OutstandingPermitCount,
            r.OutgoingConsentEvent,
            r.OutgoingRevokeEvent,
            r.OutgoingAuthorityVersion,
            r.OutgoingSeatHash,
            r.IncomingConsentEvent,
            r.IncomingRevokeEvent,
            r.IncomingAuthorityVersion,
            r.IncomingSeatHash,
            r.IncomingActiveMembers,
            r.RequestedEarlyStartAt,
            r.PaidCutoverAt,
            r.NormalRenewalAt,
            r.OverlapDisposition,
        });

    private static string ConsentLabel(Guid? accepted, Guid? revoked) =>
        revoked is not null ? "Revoked" : accepted is not null ? "Accepted" : "AwaitingConsent";

    private static string? SqlState(Exception error) =>
        (error as PostgresException ?? error.InnerException as PostgresException)?.SqlState;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Coverage replacement exception storage unavailable ({Cause}).")]
    private static partial void LogUnavailable(ILogger logger, string cause);

    private sealed record HeadRevisions(long AccountRev, long OutRev, long InRev);

    private sealed record AdminLiveContext(
        CapacityChangeSource? Source, long GrantScopeVersion, long AccountRev, long OutRev, long InRev,
        DateTimeOffset? LivePermitDeadline, DateTimeOffset? LastContactAt, DateTimeOffset? LastSynchronizedAt,
        int OutstandingPermitCount, bool AnySuspended);

    private sealed record ExceptionDetailRow(
        Guid ExceptionId, Guid AccountId, Guid TermId, long ProposalRevision, string TermsDigest,
        Guid OutgoingShopId, string OutgoingShopName, Guid OutgoingAffiliationId,
        Guid IncomingShopId, string IncomingShopName, Guid IncomingAffiliationId,
        int Capacity, int MemberAllowance,
        DateTimeOffset NormalRenewalAt, DateTimeOffset RequestedEarlyStartAt, DateTimeOffset PaidCutoverAt,
        string OverlapDisposition, DateTimeOffset? ObservedPermitDeadline, DateTimeOffset ObservedAt,
        DateTimeOffset? LastContactAt, DateTimeOffset? LastSynchronizedAt, int ObservedPermitCount,
        string RequestReason, DateTimeOffset ExpiresAt, Guid RequesterId, int RequesterRevision,
        string State, string? Reason,
        Guid? OutgoingConsentEvent, Guid? OutgoingRevokeEvent, Guid? OutgoingActorId, long? OutgoingAuthorityVersion,
        int? OutgoingActiveMembers, string? OutgoingSeatHash, DateTimeOffset? OutgoingConsentedAt,
        Guid? IncomingConsentEvent, Guid? IncomingRevokeEvent, Guid? IncomingActorId, long? IncomingAuthorityVersion,
        int? IncomingActiveMembers, string? IncomingSeatHash, DateTimeOffset? IncomingConsentedAt,
        bool OwnerCurrent, bool IsStale);

    private sealed record ApprovalRow(
        Guid ApprovalId, Guid ExceptionId, Guid AccountId, Guid TermId, long Generation, Guid OperationKey,
        Guid AdminId, string PayloadDigest, string Disposition, string Reason, DateTimeOffset DecidedAt,
        string State, string? OutcomeReason, int? MerchantPermitCount, DateTimeOffset? MerchantMaxPermitEnd,
        Guid? CourtesyGrantId, Guid? CourtesyTermId, DateTimeOffset? CourtesyStartsAt, DateTimeOffset? CourtesyEndsAt,
        DateTimeOffset PaidCutoverAt, DateTimeOffset NormalRenewalAt, DateTimeOffset? AppliedAt,
        string OutgoingActivation, string IncomingActivation, string CourtesyActivation)
    {
        internal AdminCoverageExceptionApprovalV1 ToView() => new(
            ApprovalId, ExceptionId, AccountId, TermId, Generation, OperationKey, AdminId, Disposition, Reason,
            DecidedAt, State, OutcomeReason, MerchantPermitCount, MerchantMaxPermitEnd, CourtesyGrantId, CourtesyTermId,
            CourtesyStartsAt, CourtesyEndsAt, PaidCutoverAt, NormalRenewalAt,
            OutgoingActivation, IncomingActivation, CourtesyActivation, AppliedAt);
    }
}
