using ErrorOr;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

/// <summary>Recovers a pinned attempt through provider reads before opening the account-fenced apply transaction.</summary>
public sealed class ReconcilePaymentHandler(IPaymentStore store, IPaymentGatewayResolver gateways,
    FulfilPaidOrderHandler fulfil, IPendingPaymentAttemptStore pending, TimeProvider clock, TimeProvider leaseClock)
{
    /// <summary>
    /// Reconciles one attempt. A request carrying a browser claim never resolves uncertainty; only a proof-free check
    /// may, and only on the adapter's fresh confirmation that the original order is unpaid.
    /// </summary>
    public Task<ErrorOr<bool>> HandleAsync(StoredPaymentAttempt attempt, CancellationToken ct,
        IReadOnlyDictionary<string, string>? launcherProof = null, bool launcherClaim = false, bool successClaim = false) =>
        ReconcileAsync(attempt, false, null, new(launcherProof, launcherClaim || launcherProof is not null || successClaim,
            successClaim), ct);

    public async Task<ErrorOr<bool>> HandleLeaseAsync(PendingPaymentLease lease, CancellationToken ct)
    {
        var loaded = await store.InAttemptScopeAsync<(StoredPaymentAttempt Attempt, bool Verified)>(lease.AccountId,
            lease.AttemptId, false, async token =>
            {
                var attempt = await store.FindAttemptAsync(lease.AccountId, lease.AttemptId, false, token);
                if (attempt is null)
                    return CheckoutPaymentErrors.Missing;
                return (attempt, await store.HasVerifiedPaymentAsync(lease.AttemptId, token));
            }, ct);
        return loaded.IsError ? loaded.Errors : await ReconcileAsync(loaded.Value.Attempt, loaded.Value.Verified, lease, Claim.None, ct);
    }

    private async Task<ErrorOr<bool>> ReconcileAsync(StoredPaymentAttempt attempt, bool verified, PendingPaymentLease? lease,
        Claim claim, CancellationToken ct)
    {
        // A proof-free read may only resume what was uncertain before it began; the fence is taken before the network.
        long? fence = null;
        if (!verified && !claim.Present)
        {
            var counted = await store.InAttemptScopeAsync<long>(attempt.AccountId, attempt.AttemptId, false,
                async token => await store.CountVerificationRequestsAsync(attempt.AttemptId, token), ct);
            if (counted.IsError)
                return counted.Errors;
            fence = counted.Value;
        }
        var observed = verified ? null : await ObserveAsync(attempt, claim, ct);
        return await store.InAttemptScopeAsync<bool>(attempt.AccountId, attempt.AttemptId, true, async token =>
        {
            var now = leaseClock.GetUtcNow();
            if (lease is not null && !await pending.LockAsync(lease, now, token))
                return false;
            var result = await ApplyAsync(attempt, observed, fence, token);
            if (result.IsError)
                return result;
            if (lease is not null)
                await pending.FinishAsync(lease, now, result.Value, token);
            else if (result.Value)
                await pending.CompleteAsync(token);
            return result;
        }, ct);
    }

    private async Task<RecoveryObservation?> ObserveAsync(StoredPaymentAttempt attempt, Claim claim, CancellationToken ct)
    {
        if (gateways.ForPin(attempt.Pin) is not { } gateway || gateway.Pin != attempt.Pin)
            return null;

        CheckoutSession? session = null;
        var reference = attempt.ProviderOrderReference;
        if (reference is null)
        {
            var request = Request(attempt);
            var lookup = await PaymentGatewayCall.RunAsync(token => gateway.LookupCheckoutAsync(request, token), ct);
            if (lookup is not { Outcome: GatewayOutcome.Succeeded, Value: { } found } ||
                found.Pin != attempt.Pin || found.Request != request || !ValidSession(found.Session))
                return null;
            session = found.Session;
            reference = session.ProviderOrderReference;
        }

        var verification = new CheckoutVerification(reference, attempt.AmountPaise, attempt.Currency,
            Request(attempt), claim.Proof, claim.Success);
        var result = await PaymentGatewayCall.RunAsync(token => gateway.VerifyCheckoutAsync(verification, token), ct);
        return new(session, result.Outcome == GatewayOutcome.Succeeded ? result.Value : null);
    }

    private async Task<ErrorOr<bool>> ApplyAsync(StoredPaymentAttempt attempt, RecoveryObservation? observed, long? fence,
        CancellationToken ct)
    {
        if (await store.HasVerifiedPaymentAsync(attempt.AttemptId, ct))
            return true;
        if (observed is null)
            return await store.HasDefinitiveFailureAsync(attempt.AttemptId, ct);

        var stored = await store.FindAttemptAsync(attempt.AccountId, attempt.AttemptId, false, ct);
        if (stored is null)
            return CheckoutPaymentErrors.Missing;

        if (observed.Session is { } found && stored.ProviderOrderReference is null)
        {
            var now = BillingAccountRules.StorageInstant(clock.GetUtcNow());
            if (!await store.RecordSessionAsync(stored, found, now, ct))
                return CheckoutPaymentErrors.Busy;
            await store.AppendEventAsync(stored, PaymentEventKinds.SessionRecorded, null, now, ct);
            stored = stored with { ProviderOrderReference = found.ProviderOrderReference };
        }

        // An unpaid order stays payable; payment evidence that does not fund it makes the outcome uncertain.
        if (observed.Payment is { State: ObservedPaymentState.Captured } payment)
        {
            var result = await fulfil.ApplyAsync(stored, payment, ct);
            if (!result.IsError && result.Value == FulfilmentOutcome.Rejected)
                await MarkUncertainAsync(stored, ct);
            return result.IsError ? result.Errors : result.Value != FulfilmentOutcome.Rejected;
        }
        if (observed.Payment is not { State: ObservedPaymentState.Pending, FundingAmbiguous: false } pending)
            await MarkUncertainAsync(stored, ct);
        else if (fence is { } before && Resumable(stored, pending) && await ResumableSinceAsync(stored, before, ct))
            await store.AppendEventAsync(stored, PaymentEventKinds.ResumeConfirmed, null,
                BillingAccountRules.StorageInstant(clock.GetUtcNow()), ct);
        if (await store.HasDefinitiveFailureAsync(attempt.AttemptId, ct))
            return true;
        if (!DefinitivelyFailed(stored, observed.Payment))
            return false;
        await store.AppendEventAsync(stored, PaymentEventKinds.CheckoutFailed, null,
            BillingAccountRules.StorageInstant(clock.GetUtcNow()), ct);
        return true;
    }

    /// <summary>Uncertainty is outstanding and none was recorded after the fence: the read is not older than the doubt.</summary>
    private async Task<bool> ResumableSinceAsync(StoredPaymentAttempt attempt, long fence, CancellationToken ct) =>
        await store.IsUncertainAsync(attempt.AttemptId, ct) &&
        await store.CountVerificationRequestsAsync(attempt.AttemptId, ct) == fence;

    private Task MarkUncertainAsync(StoredPaymentAttempt attempt, CancellationToken ct) =>
        store.AppendEventAsync(attempt, PaymentEventKinds.VerificationRequested, null,
            BillingAccountRules.StorageInstant(clock.GetUtcNow()), ct);

    private static bool DefinitivelyFailed(StoredPaymentAttempt attempt, PaymentObservation? observation) =>
        observation is { State: ObservedPaymentState.Failed, CheckoutDefinitivelyFailed: true,
            ProviderPaymentReference: null, CapturedAt: null } &&
        observation.Pin == attempt.Pin && observation.ProviderOrderReference == attempt.ProviderOrderReference &&
        observation.AmountPaise == attempt.AmountPaise && observation.Currency == attempt.Currency;

    /// <summary>The adapter proved this attempt's own order open and unpaid, on its own pin, for the full amount.</summary>
    private static bool Resumable(StoredPaymentAttempt attempt, PaymentObservation observation) =>
        observation is { State: ObservedPaymentState.Pending, ConfirmedUnpaid: true, ProviderPaymentReference: null,
            CapturedAt: null, CheckoutDefinitivelyFailed: false } &&
        observation.Pin == attempt.Pin && observation.ProviderOrderReference == attempt.ProviderOrderReference &&
        observation.AmountPaise == attempt.AmountPaise && observation.Currency == attempt.Currency;

    internal static CheckoutRequest Request(StoredPaymentAttempt attempt) =>
        new(attempt.AccountId, attempt.OrderId, attempt.AttemptId, attempt.AmountPaise, attempt.Currency);

    internal static bool ValidSession(CheckoutSession session) =>
        session.ProviderOrderReference is { Length: > 0 and <= 120 } &&
        CheckoutPaymentsV1.LauncherFlows.Contains(session.LauncherFlow);

    private sealed record RecoveryObservation(CheckoutSession? Session, PaymentObservation? Payment);

    /// <summary>The browser's report, if any: its optional proof, and whether it reported success.</summary>
    private sealed record Claim(IReadOnlyDictionary<string, string>? Proof, bool Present, bool Success)
    {
        internal static readonly Claim None = new(null, false, false);
    }
}
