using ErrorOr;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

/// <summary>Recovers a pinned attempt through provider reads before opening the account-fenced apply transaction.</summary>
public sealed class ReconcilePaymentHandler(IPaymentStore store, IPaymentGatewayResolver gateways,
    FulfilPaidOrderHandler fulfil, IPendingPaymentAttemptStore pending, TimeProvider clock, TimeProvider leaseClock)
{
    public Task<ErrorOr<bool>> HandleAsync(StoredPaymentAttempt attempt, CancellationToken ct,
        IReadOnlyDictionary<string, string>? launcherProof = null) =>
        ReconcileAsync(attempt, false, null, ct, launcherProof);

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
        return loaded.IsError ? loaded.Errors : await ReconcileAsync(loaded.Value.Attempt, loaded.Value.Verified, lease, ct);
    }

    private async Task<ErrorOr<bool>> ReconcileAsync(StoredPaymentAttempt attempt, bool verified, PendingPaymentLease? lease,
        CancellationToken ct, IReadOnlyDictionary<string, string>? launcherProof = null)
    {
        var observed = verified ? null : await ObserveAsync(attempt, launcherProof, ct);
        return await store.InAttemptScopeAsync<bool>(attempt.AccountId, attempt.AttemptId, true, async token =>
        {
            var now = leaseClock.GetUtcNow();
            if (lease is not null && !await pending.LockAsync(lease, now, token))
                return false;
            var result = await ApplyAsync(attempt, observed, token);
            if (result.IsError)
                return result;
            if (lease is not null)
                await pending.FinishAsync(lease, now, result.Value, token);
            else if (result.Value)
                await pending.CompleteAsync(token);
            return result;
        }, ct);
    }

    private async Task<RecoveryObservation?> ObserveAsync(StoredPaymentAttempt attempt,
        IReadOnlyDictionary<string, string>? launcherProof, CancellationToken ct)
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
            Request(attempt), launcherProof);
        var result = await PaymentGatewayCall.RunAsync(token => gateway.VerifyCheckoutAsync(verification, token), ct);
        return new(session, result.Outcome == GatewayOutcome.Succeeded ? result.Value : null);
    }

    private async Task<ErrorOr<bool>> ApplyAsync(StoredPaymentAttempt attempt, RecoveryObservation? observed,
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
            await store.AppendEventAsync(stored, PaymentEventKinds.VerificationRequested, null, now, ct);
            stored = stored with { ProviderOrderReference = found.ProviderOrderReference };
        }

        if (observed.Payment is { State: ObservedPaymentState.Captured } payment)
        {
            var result = await fulfil.ApplyAsync(stored, payment, ct);
            return result.IsError ? result.Errors : result.Value != FulfilmentOutcome.Rejected;
        }
        if (await store.HasDefinitiveFailureAsync(attempt.AttemptId, ct))
            return true;
        if (!DefinitivelyFailed(stored, observed.Payment))
            return false;
        await store.AppendEventAsync(stored, PaymentEventKinds.CheckoutFailed, null,
            BillingAccountRules.StorageInstant(clock.GetUtcNow()), ct);
        return true;
    }

    private static bool DefinitivelyFailed(StoredPaymentAttempt attempt, PaymentObservation? observation) =>
        observation is { State: ObservedPaymentState.Failed, CheckoutDefinitivelyFailed: true,
            ProviderPaymentReference: null, CapturedAt: null } &&
        observation.Pin == attempt.Pin && observation.ProviderOrderReference == attempt.ProviderOrderReference &&
        observation.AmountPaise == attempt.AmountPaise && observation.Currency == attempt.Currency;

    internal static CheckoutRequest Request(StoredPaymentAttempt attempt) =>
        new(attempt.AccountId, attempt.OrderId, attempt.AttemptId, attempt.AmountPaise, attempt.Currency);

    internal static bool ValidSession(CheckoutSession session) =>
        session.ProviderOrderReference is { Length: > 0 and <= 120 } &&
        CheckoutPaymentsV1.LauncherFlows.Contains(session.LauncherFlow);

    private sealed record RecoveryObservation(CheckoutSession? Session, PaymentObservation? Payment);
}
