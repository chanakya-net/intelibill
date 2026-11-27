using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Coverage;

public sealed class RequestCoverageReplacementExceptionHandler(
    IBillingDelegationAccess access, ICoverageReplacementExceptionStore store)
{
    public Task<ErrorOr<CoverageReplacementExceptionPreviewV1>> ReadAsync(VerifiedBillingDelegation proof,
        Guid account, Guid term, int page, CancellationToken ct) =>
        access.ExecuteAsync<CoverageReplacementExceptionPreviewV1>(proof,
            (_, token) => store.ReadCustomerAsync(account, term, page, token), ct);

    public Task<ErrorOr<CoverageReplacementExceptionV1>> RequestAsync(VerifiedBillingDelegation proof,
        Guid account, Guid term, RequestCoverageReplacementExceptionV1 command, CancellationToken ct) =>
        access.ExecuteAsync<CoverageReplacementExceptionV1>(proof,
            (scope, token) => IsValidRequest(command)
                ? store.RequestAsync(account, term, scope.ActorId, command, token)
                : Task.FromResult<ErrorOr<CoverageReplacementExceptionV1>>(CoverageReplacementExceptionErrors.Invalid), ct);

    public Task<ErrorOr<CoverageReplacementExceptionV1>> RecoverAsync(VerifiedBillingDelegation proof,
        Guid account, Guid term, Guid key, CancellationToken ct) =>
        access.ExecuteAsync<CoverageReplacementExceptionV1>(proof,
            (scope, token) => store.RecoverCustomerAsync(account, term, scope.ActorId, key, token), ct);

    public Task<ErrorOr<CoverageReplacementExceptionV1>> OwnerAsync(VerifiedBillingDelegation proof,
        Guid account, Guid shop, Guid exceptionId, CancellationToken ct) =>
        access.ExecuteAsync<CoverageReplacementExceptionV1>(proof,
            (_, token) => store.OwnerReadAsync(account, shop, exceptionId, token), ct);

    private static bool IsValidRequest(RequestCoverageReplacementExceptionV1 c) =>
        c.OperationKey != Guid.Empty && c.OutgoingShopId != Guid.Empty && c.IncomingShopId != Guid.Empty &&
        c.OutgoingShopId != c.IncomingShopId && c.ObservedCapacity > 0 && c.ObservedMemberAllowance > 0 &&
        EntitlementSnapshotsV1.IsInstant(c.RequestedEarlyStartAt) &&
        EntitlementSnapshotsV1.IsInstant(c.ProposedPaidCutoverAt) &&
        EntitlementSnapshotsV1.IsInstant(c.ObservedNormalRenewalAt) &&
        c.RequestedEarlyStartAt <= c.ProposedPaidCutoverAt &&
        c.ProposedPaidCutoverAt < c.ObservedNormalRenewalAt &&
        CoverageReplacementExceptionsV1.IsDisposition(c.OverlapDisposition) &&
        (c.OverlapDisposition != CoverageReplacementExceptionsV1.CourtesyBridgeDisposition ||
            c.RequestedEarlyStartAt < c.ProposedPaidCutoverAt) &&
        (c.ObservedPermitDeadline is null ||
            (EntitlementSnapshotsV1.IsInstant(c.ObservedPermitDeadline.Value) &&
             c.ProposedPaidCutoverAt >= c.ObservedPermitDeadline.Value)) &&
        CoverageReplacementExceptionsV1.NormalizeReason(c.Reason) is not null &&
        c.AcknowledgedShopSelection && c.AcknowledgedRemainingAccessAllocation &&
        c.AcknowledgedExistingPermits && c.AcknowledgedUnknownDisconnectedQueues;
}

/// <summary>
/// Provider review, approval and same-operation recovery for early coverage-replacement exceptions.
/// Live Licensing Manager identity/session and recent MFA are re-verified before any receipt lookup,
/// after the billing-account lock wait and again before commit. An identical actor/account/key/digest
/// returns the original receipt ahead of current-version checks; mismatched payload or another actor
/// fails without disclosing the stored decision.
/// </summary>
public sealed class ApproveCoverageReplacementHandler(
    ICoverageReplacementExceptionStore store, TimeProvider lifecycleClock, TimeProvider sessionClock)
{
    public Task<ErrorOr<AdminCoverageExceptionListV1>> ListAsync(AdminSessionCaller caller, Guid account,
        CancellationToken ct) => store.InAdminTransactionAsync<AdminCoverageExceptionListV1>(async token =>
    {
        if (await AuthorizeAsync(caller, recentMfa: false, token) is { Error: { } denied }) return denied;
        if (account == Guid.Empty) return CoverageReplacementExceptionErrors.Invalid;
        await store.EnterAdminAsync(caller.AdminId, caller.SessionId, account, token);
        return await store.ListAdminAsync(account, BusinessNow(), token);
    }, ct);

    public Task<ErrorOr<AdminCoverageExceptionPreviewV1>> PreviewAsync(AdminSessionCaller caller, Guid account,
        Guid exceptionId, CancellationToken ct) => store.InAdminTransactionAsync<AdminCoverageExceptionPreviewV1>(async token =>
    {
        if (await AuthorizeAsync(caller, recentMfa: false, token) is { Error: { } denied }) return denied;
        if (account == Guid.Empty || exceptionId == Guid.Empty) return CoverageReplacementExceptionErrors.Invalid;
        await store.EnterAdminAsync(caller.AdminId, caller.SessionId, account, token);
        return await store.PreviewAdminAsync(account, exceptionId, BusinessNow(), token);
    }, ct);

    public Task<ErrorOr<CoverageExceptionApprovalOutcome>> ApproveAsync(AdminSessionCaller caller, Guid account,
        Guid exceptionId, ApproveCoverageReplacementRequestV1? request, CancellationToken ct) =>
        store.InAdminTransactionAsync<CoverageExceptionApprovalOutcome>(async token =>
        {
            var initial = await AuthorizeAsync(caller, recentMfa: true, token);
            if (initial.Error is { } denied) return denied;
            var reason = ValidateRequest(account, exceptionId, request);
            if (reason is null) return CoverageReplacementExceptionErrors.Invalid;
            var digest = PayloadDigest(account, exceptionId, request!, reason);

            await store.EnterAdminAsync(caller.AdminId, caller.SessionId, account, token);
            await store.LockAccountAsync(account, token);
            var afterLock = await AuthorizeAsync(caller, recentMfa: true, token);
            if (afterLock.Error is { } expired) return expired;

            var now = BusinessNow();
            if (await store.FindApprovalByKeyAsync(account, request!.OperationKey, now, token) is { } prior)
                return prior.AdminId == caller.AdminId && prior.ExceptionId == exceptionId && prior.PayloadDigest == digest
                    ? new CoverageExceptionApprovalOutcome(prior.View, Created: false)
                    : CoverageReplacementExceptionErrors.PayloadMismatch;

            var previewResult = await store.PreviewAdminAsync(account, exceptionId, now, token);
            if (previewResult.IsError) return previewResult.Errors;
            var preview = previewResult.Value;
            if (!MatchesPreview(preview, request)) return CoverageReplacementExceptionErrors.Changed;
            if (!preview.CanApprove) return CoverageReplacementExceptionErrors.ConsentsRequired;

            var saved = await store.RecordApprovalAndQueuePrepareAsync(account, exceptionId, caller,
                afterLock.MfaVerifiedAt!.Value, afterLock.AuthorizedAt!.Value, now, request, reason, digest, preview, token);
            if (saved.IsError) return saved.Errors;

            var finalAuth = await AuthorizeAsync(caller, recentMfa: true, token);
            if (finalAuth.Error is { } lost) return lost;
            return new CoverageExceptionApprovalOutcome(saved.Value, Created: true);
        }, ct);

    public Task<ErrorOr<AdminCoverageExceptionApprovalV1>> RecoverAsync(AdminSessionCaller caller, Guid account,
        Guid exceptionId, Guid operationKey, CancellationToken ct) =>
        store.InAdminTransactionAsync<AdminCoverageExceptionApprovalV1>(async token =>
        {
            if (await AuthorizeAsync(caller, recentMfa: true, token) is { Error: { } denied }) return denied;
            if (account == Guid.Empty || exceptionId == Guid.Empty || operationKey == Guid.Empty)
                return CoverageReplacementExceptionErrors.NotFound;
            await store.EnterAdminAsync(caller.AdminId, caller.SessionId, account, token);
            var prior = await store.FindApprovalByKeyAsync(account, operationKey, BusinessNow(), token);
            return prior is not null && prior.AdminId == caller.AdminId && prior.ExceptionId == exceptionId
                ? prior.View : CoverageReplacementExceptionErrors.NotFound;
        }, ct);

    private static string? ValidateRequest(Guid account, Guid exceptionId, ApproveCoverageReplacementRequestV1? r)
    {
        if (r is not { Confirmed: true, AcknowledgedPermitsAndUnknownQueues: true } ||
            account == Guid.Empty || exceptionId == Guid.Empty || r.OperationKey == Guid.Empty ||
            r.ExpectedProposalRevision <= 0 || r.ExpectedGrantVersion < 0 ||
            !EntitlementSnapshotsV1.IsDigest(r.ExpectedSourceHash) ||
            !EntitlementSnapshotsV1.IsDigest(r.ExpectedTermsDigest) ||
            !CoverageReplacementExceptionsV1.IsDisposition(r.Disposition))
            return null;
        return CoverageReplacementExceptionsV1.NormalizeReason(r.Reason);
    }

    private static bool MatchesPreview(AdminCoverageExceptionPreviewV1 p, ApproveCoverageReplacementRequestV1 r) =>
        p.ProposalRevision == r.ExpectedProposalRevision &&
        p.SourceHash == r.ExpectedSourceHash &&
        p.TermsDigest == r.ExpectedTermsDigest &&
        p.GrantScopeVersion == r.ExpectedGrantVersion &&
        p.OverlapDisposition == r.Disposition;

    private static string PayloadDigest(Guid account, Guid exceptionId, ApproveCoverageReplacementRequestV1 r, string reason) =>
        CheckoutOrdersV1.Digest("coverage-replacement-exception-approval/1", new
        {
            account,
            exceptionId,
            r.OperationKey,
            r.ExpectedProposalRevision,
            r.ExpectedSourceHash,
            r.ExpectedTermsDigest,
            r.ExpectedGrantVersion,
            r.Disposition,
            reason,
            r.AcknowledgedPermitsAndUnknownQueues,
            r.Confirmed,
        });

    private DateTimeOffset BusinessNow() => SubscriptionStatusProjection.EvaluationInstant(lifecycleClock);

    internal async Task<(Error? Error, DateTimeOffset? MfaVerifiedAt, DateTimeOffset? AuthorizedAt)> AuthorizeAsync(
        AdminSessionCaller caller, bool recentMfa, CancellationToken ct)
    {
        var (actor, session) = await store.LockProviderAsync(caller, ct);
        var now = SubscriptionStatusProjection.EvaluationInstant(sessionClock);
        if (session is null || now >= caller.AccessExpiresAt || !session.IsLiveAt(now)
            || actor is not { DeactivatedAt: null, MfaEnrolledAt: not null })
            return (AdminRefreshErrors.SessionEnded, null, null);
        if (!actor.Capabilities.HasFlag(AdminCapability.LicensingManager))
            return (CoverageReplacementExceptionErrors.AccessDenied, null, null);
        if (recentMfa && !new ProviderActor(caller.AdminId, session.MfaVerifiedAt).HasRecentMfa(now))
            return (CoverageReplacementExceptionErrors.RecentMfa, null, null);
        return (null, session.MfaVerifiedAt, now);
    }
}
