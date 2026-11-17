namespace Intelibill.Licensing.Application.Payments;

/// <summary>Only capture notifications trigger independent verification. Other notifications are retained as evidence.</summary>
public sealed class ProcessGatewayInboxHandler(IGatewayInboxStore inbox, ReconcilePaymentHandler reconcile, TimeProvider clock)
{
    public async Task<GatewayInboxResolution?> HandleAsync(GatewayInboxLease lease, CancellationToken ct)
    {
        if (lease.Kind != GatewayEventKinds.PaymentCaptured || lease.OrderReference is null)
            return await FinishAsync(lease, GatewayInboxResolution.Processed(GatewayInboxOutcome.NoAction), ct);
        if (lease.Candidates == 0)
            return await FinishAsync(lease, GatewayInboxResolution.Retry(GatewayInboxFailure.UnknownOrder), ct);
        if (lease.Candidates != 1 || lease.AccountId is null || lease.AttemptId is null)
            return await FinishAsync(lease, GatewayInboxResolution.Dead(GatewayInboxFailure.AmbiguousOrder), ct);

        var reconciled = await reconcile.HandleInboxLeaseAsync(inbox, lease, lease.AccountId.Value, lease.AttemptId.Value, ct);
        if (!reconciled.IsError)
            return reconciled.Value;

        var failure = reconciled.FirstError == CheckoutPaymentErrors.Busy
            ? GatewayInboxFailure.Busy : GatewayInboxFailure.Unavailable;
        return await FinishAsync(lease, GatewayInboxResolution.Retry(failure), ct);
    }

    private async Task<GatewayInboxResolution?> FinishAsync(GatewayInboxLease lease, GatewayInboxResolution resolution,
        CancellationToken ct) => await inbox.FinishAsync(lease, resolution, clock.GetUtcNow(), ct) ? resolution : null;
}
