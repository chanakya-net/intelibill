using ErrorOr;

namespace Intelibill.Licensing.Application.Payments;

public sealed partial class ReconcilePaymentHandler
{
    internal async Task<ErrorOr<GatewayInboxResolution?>> HandleInboxLeaseAsync(IGatewayInboxStore inbox,
        GatewayInboxLease lease, Guid account, Guid id, CancellationToken ct)
    {
        var loaded = await store.InAttemptScopeAsync<(StoredPaymentAttempt Attempt, bool Verified)>(account, id, false, async token =>
        {
            var attempt = await store.FindAttemptAsync(account, id, false, token);
            if (attempt is null || attempt.ProviderOrderReference != lease.OrderReference)
                return CheckoutPaymentErrors.Missing;
            return (attempt, await store.HasVerifiedPaymentAsync(id, token));
        }, ct);
        if (loaded.IsError)
            return loaded.Errors;

        var (original, verified) = loaded.Value;
        var gateway = verified ? null : gateways.ForPin(original.Pin);
        var available = gateway is not null && gateway.Pin == original.Pin;
        var observed = available ? await ObserveAsync(original, Claim.None, ct, gateway) : null;

        return await store.InAttemptScopeAsync<GatewayInboxResolution?>(account, id, true, async token =>
        {
            var now = leaseClock.GetUtcNow();
            if (!await inbox.LockAsync(lease, now, token))
                return (GatewayInboxResolution?)null;

            var result = await ApplyCaptureNotificationAsync(original, lease, observed?.Payment, available, token);
            if (result.IsError)
                return result.Errors;
            if (!await inbox.FinishAsync(lease, result.Value, now, token))
                return CheckoutPaymentErrors.Busy;
            if (result.Value.Outcome is GatewayInboxOutcome.Fulfilled or GatewayInboxOutcome.VerifiedNeedsReview
                or GatewayInboxOutcome.AlreadyVerified || result.Value.Failure == GatewayInboxFailure.DifferentPayment)
                await pending.CompleteAsync(token);
            return result.Value;
        }, ct);
    }

    private async Task<ErrorOr<GatewayInboxResolution>> ApplyCaptureNotificationAsync(StoredPaymentAttempt attempt,
        GatewayInboxLease lease, PaymentObservation? payment, bool adapterAvailable, CancellationToken ct)
    {
        if (await store.FindVerifiedPaymentReferenceAsync(attempt.AttemptId, ct) is { } existing)
            return VerifiedResolution(lease, existing, GatewayInboxOutcome.AlreadyVerified);
        if (!adapterAvailable)
            return GatewayInboxResolution.Retry(GatewayInboxFailure.AdapterUnavailable);

        if (payment is { State: ObservedPaymentState.Captured })
        {
            var settled = await fulfil.ApplyAsync(attempt, payment, ct);
            if (settled.IsError)
                return settled.Errors;
            if (settled.Value is FulfilmentOutcome.NeedsReview or FulfilmentOutcome.Fulfilled)
            {
                var outcome = settled.Value == FulfilmentOutcome.Fulfilled
                    ? GatewayInboxOutcome.Fulfilled : GatewayInboxOutcome.VerifiedNeedsReview;
                return VerifiedResolution(lease, payment.ProviderPaymentReference!, outcome);
            }
        }

        // A capture notification warrants a recheck. A stale unpaid observation cannot resolve it.
        if (!await store.IsUncertainAsync(attempt.AttemptId, ct) && !await store.HasDefinitiveFailureAsync(attempt.AttemptId, ct))
            await MarkUncertainAsync(attempt, ct);
        return GatewayInboxResolution.Retry(GatewayInboxFailure.CaptureUnproven);
    }

    private static GatewayInboxResolution VerifiedResolution(GatewayInboxLease lease, string reference, GatewayInboxOutcome outcome) =>
        lease.PaymentReference is null || lease.PaymentReference == reference
            ? GatewayInboxResolution.Processed(outcome) : GatewayInboxResolution.Dead(GatewayInboxFailure.DifferentPayment);
}
