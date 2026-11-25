using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Application.Entitlements;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Refunds;

/// <summary>Provider credential time judges MFA; lifecycle time dates the commercial and access decision.</summary>
public sealed record RefundApprovalClocks(TimeProvider Session, TimeProvider Lifecycle);

/// <summary>
/// Approves one refund request under live Billing authority and recent MFA. The amount is reserved under the account
/// fence and the original payment's receipt lock, capped by the request and by what the verified receipt still has
/// free; the explicit access decision is recorded separately in the same transaction. Authorization precedes replay.
/// Nothing is dispatched to a provider here and no access changes unless the operator chose an effective treatment.
/// </summary>
public sealed class ApproveRefundHandler(IManualPaymentStore admins, IRefundApprovalStore store,
    IEntitlementPublisher publisher, RefundApprovalClocks clocks)
{
    public Task<ErrorOr<RefundApprovalPreview>> PreviewAsync(AdminSessionCaller caller, Guid account, Guid request,
        PreviewRefundApproval? command, CancellationToken ct) =>
        admins.InManualScopeAsync<RefundApprovalPreview>(caller, account, Guid.Empty, recentMfa: true,
            lockAccount: command?.Treatment is RefundApprovalRules.EndAt or RefundApprovalRules.TermAdjustment,
            async (_, token) =>
            {
                if (command is null || command.AmountPaise is <= 0 or > CheckoutOrdersV1.MaxPaise
                    || !RefundApprovalRules.HasDates(command.Treatment, command.EffectiveAt))
                    return RefundApprovalErrors.Invalid;
                if (await store.SourceAsync(account, request, token) is not { } source)
                    return RefundApprovalErrors.RequestMissing;
                if (source.Decided) return RefundApprovalErrors.AlreadyDecided;
                return await ReviewAsync(source, command.AmountPaise, command.Treatment!, command.EffectiveAt, token);
            }, ct);

    public Task<ErrorOr<RefundApprovalOutcome>> ApproveAsync(AdminSessionCaller caller, Guid account, Guid request,
        ApproveRefund? command, CancellationToken ct) =>
        admins.InManualScopeAsync<RefundApprovalOutcome>(caller, account, Guid.Empty, recentMfa: true, lockAccount: true,
            (mfa, token) => ApproveLockedAsync(caller, account, request, command, mfa, token), ct);

    public Task<ErrorOr<RefundApprovalReceipt>> RecoverAsync(AdminSessionCaller caller, Guid account, Guid request,
        Guid operationKey, CancellationToken ct) =>
        admins.InManualScopeAsync<RefundApprovalReceipt>(caller, account, Guid.Empty, recentMfa: true, lockAccount: false,
            async (_, token) =>
                await store.FindAsync(account, operationKey, token) is { Receipt: var receipt }
                && receipt.RefundRequestId == request && receipt.AdminId == caller.AdminId
                    ? receipt : RefundApprovalErrors.OperationMissing, ct);

    private async Task<ErrorOr<RefundApprovalOutcome>> ApproveLockedAsync(AdminSessionCaller caller, Guid account,
        Guid request, ApproveRefund? command, DateTimeOffset mfa, CancellationToken ct)
    {
        if (command is null || command.OperationKey == Guid.Empty
            || !RefundApprovalRules.HasDates(command.Treatment, command.EffectiveAt)
            || command.AmountPaise is <= 0 or > CheckoutOrdersV1.MaxPaise || command.ExpectedVersion is null or < 0 || !command.Confirmed
            || command.EffectiveAt is { } at && at != BillingAccountRules.StorageInstant(at)
            || !CheckoutOrdersV1.IsHash(command.ImpactDigest)
            || RefundRequestsV1.NormalizeReason(command.Reason) is not { } reason)
            return RefundApprovalErrors.Invalid;

        // An exact replay answers with the saved receipt before any current balance or version is consulted.
        var hash = RefundApprovalRules.PayloadHash(request, command, reason);
        if (await store.FindAsync(account, command.OperationKey, ct) is { } prior)
            return prior.Receipt.RefundRequestId == request && prior.Receipt.AdminId == caller.AdminId
                && prior.PayloadHash == hash ? new RefundApprovalOutcome(prior.Receipt, false)
                : RefundApprovalErrors.PayloadMismatch;
        if (await store.SourceAsync(account, request, ct) is not { } source) return RefundApprovalErrors.RequestMissing;

        // Lock the original payment first, then read its ledger in a subsequent statement.
        await store.LockReceiptAsync(source.ConfirmationId, ct);
        var authorized = BillingAccountRules.StorageInstant(clocks.Session.GetUtcNow());
        if (!new ProviderActor(caller.AdminId, mfa).HasRecentMfa(authorized)) return ManualPaymentErrors.RecentMfa;
        if ((await store.SourceAsync(account, request, ct))!.Decided) return RefundApprovalErrors.AlreadyDecided;
        var review = await ReviewAsync(source, command.AmountPaise, command.Treatment!, command.EffectiveAt, ct,
            command.ExpectedVersion, command.ImpactDigest);
        if (review.IsError) return review.Errors;

        var preview = review.Value;
        var decided = preview.ReviewedAt;
        var receipt = new RefundApprovalReceipt(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), account, request,
            source.OrderId, source.ConfirmationId, command.OperationKey, RefundRequestsV1.Pending, command.AmountPaise,
            source.RequestedAmountPaise, source.PaymentAmountPaise, preview.RemainingAmountPaise,
            preview.ProposedRemainingAmountPaise, source.Currency, preview.ReceiptVersion + 1, caller.AdminId, mfa,
            decided, reason, preview.Access);
        var target = preview.Access.Treatment == RefundApprovalRules.Preserve ? null : preview.Target;
        await store.SaveAsync(new(source, receipt, caller.SessionId, authorized, hash, preview.ImpactDigest,
            target?.TermRevision + 1), ct);
        if (target is not null && source.AttemptId is null)
            foreach (var shop in target.Shops)
                await publisher.PublishAsync(account, shop.ShopId, ct, decided);
        return new RefundApprovalOutcome(receipt, true);
    }

    private async Task<ErrorOr<RefundApprovalPreview>> ReviewAsync(RefundApprovalSource source, long amount,
        string treatment, DateTimeOffset? effectiveAt, CancellationToken ct, long? expectedVersion = null,
        string? expectedDigest = null)
    {
        var ledger = await store.LedgerAsync(source.AccountId, source.ConfirmationId, ct);
        var at = Now();
        var target = await store.TargetAsync(source, at, ct);
        var access = RefundAccessPolicy.Impact(target, treatment, effectiveAt, at);
        // A reviewed preview that no longer matches is stale before any amount or treatment judgement.
        var digest = access is null ? null : RefundAccessPolicy.Digest(source, target, access, amount, ledger);
        if (expectedVersion is { } version && (version != ledger.Revision || digest != expectedDigest))
            return RefundApprovalErrors.PreviewStale;
        if (access is null) return RefundApprovalErrors.TreatmentUnavailable;

        var remaining = source.PaymentAmountPaise - ledger.OccupiedPaise;
        if (amount > source.RequestedAmountPaise || amount > remaining) return RefundApprovalErrors.AmountExceeds(remaining);
        return new RefundApprovalPreview(source.AccountId, source.RefundRequestId, source.OrderId, source.ConfirmationId,
            RefundRequestsV1.Requested, source.PaymentAmountPaise, source.RequestedAmountPaise, ledger.OccupiedPaise,
            remaining, amount, remaining - amount, source.Currency, source.AttemptId is null ? "Manual" : "Online",
            source.ProviderId, source.ProviderAccountId, source.Environment, source.ConfigurationVersion,
            source.RefundPolicyVersion, ledger.Revision, digest!, access) { Target = target, ReviewedAt = at };
    }

    private DateTimeOffset Now() => BillingAccountRules.StorageInstant(clocks.Lifecycle.GetUtcNow());
}
