using ErrorOr;

namespace Intelibill.Licensing.Application.Payments;

public sealed partial class ReconcilePaymentHandler
{
    internal async Task<ErrorOr<GatewayInboxResolution?>> HandleInboxLeaseAsync(IGatewayInboxStore inbox,
        GatewayInboxLease lease, Guid account, Guid id, CancellationToken ct)
    {
        var loaded = await store.InAttemptScopeAsync<(StoredPaymentAttempt Attempt, string? PaymentReference)>(account, id, false, async token =>
        {
            var attempt = await store.FindAttemptAsync(account, id, false, token);
            if (attempt is null || attempt.ProviderOrderReference != lease.OrderReference)
                return CheckoutPaymentErrors.Missing;
            return (attempt, await store.FindVerifiedPaymentReferenceAsync(id, token));
        }, ct);
        if (loaded.IsError)
            return loaded.Errors;

        var (original, paymentReference) = loaded.Value;
        var sameReceipt = paymentReference is not null && (lease.PaymentReference is null || lease.PaymentReference == paymentReference);
        var gateway = sameReceipt ? null : gateways.ForPin(original.Pin);
        var available = gateway is not null && gateway.Pin == original.Pin;
        var observed = available ? await ObserveNotificationAsync(original, lease, paymentReference, gateway!, ct) : null;

        return await store.InAttemptScopeAsync<GatewayInboxResolution?>(account, id, true, async token =>
        {
            var now = leaseClock.GetUtcNow();
            if (!await inbox.LockAsync(lease, now, token))
                return (GatewayInboxResolution?)null;

            var result = await ApplyCaptureNotificationAsync(original, lease, observed, available, token);
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
        GatewayInboxLease lease, RecoveryObservation? observation, bool adapterAvailable, CancellationToken ct)
    {
        var existing = await store.FindVerifiedPaymentReferenceAsync(attempt.AttemptId, ct);
        if (observation is not null && await RecordReviewCapturesAsync(attempt, observation, existing, lease.InboxId, ct))
        {
            if (existing is null && !await store.IsUncertainAsync(attempt.AttemptId, ct))
                await MarkUncertainAsync(attempt, ct);
            return GatewayInboxResolution.Processed(GatewayInboxOutcome.VerifiedNeedsReview);
        }
        if (existing is not null)
        {
            if (lease.PaymentReference is { } received && received != existing &&
                (!adapterAvailable || observation?.ReviewReadUnavailable == true))
                return GatewayInboxResolution.Retry(GatewayInboxFailure.AdapterUnavailable);
            return VerifiedResolution(lease, existing, GatewayInboxOutcome.AlreadyVerified);
        }
        if (!adapterAvailable)
        {
            await store.RecordRoutingAsync(attempt, PaymentRouting.Blocker(gateways, attempt.Pin), clock.GetUtcNow(), ct);
            return GatewayInboxResolution.Retry(GatewayInboxFailure.AdapterUnavailable);
        }
        if (observation is not null)
            await store.RecordRoutingAsync(attempt, null, clock.GetUtcNow(), ct);

        if (observation?.Payment is { State: ObservedPaymentState.Captured } payment)
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

    private async Task<RecoveryObservation?> ObserveNotificationAsync(StoredPaymentAttempt attempt, GatewayInboxLease lease,
        string? existing, IPaymentGateway gateway, CancellationToken ct)
    {
        if (existing is null)
            return await ObserveAsync(attempt, Claim.None, ct, gateway);

        var captures = await InspectAsync(gateway, attempt.ProviderOrderReference!, lease.PaymentReference, ct);
        return new(null, null, captures.Value ?? [], captures.Outcome == GatewayOutcome.Unavailable);
    }

    private static GatewayInboxResolution VerifiedResolution(GatewayInboxLease lease, string reference, GatewayInboxOutcome outcome) =>
        lease.PaymentReference is null || lease.PaymentReference == reference
            ? GatewayInboxResolution.Processed(outcome) : GatewayInboxResolution.Dead(GatewayInboxFailure.DifferentPayment);
}
