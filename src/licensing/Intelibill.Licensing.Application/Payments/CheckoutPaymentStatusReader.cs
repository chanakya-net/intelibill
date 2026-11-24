using ErrorOr;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Application.Restrictions;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

/// <summary>
/// Projects the neutral payment, fulfilment and access states of one order. Access is applied for a shop only once
/// the merchant's signed acknowledgement confirms the snapshot published by the fulfilment (or a later one). An
/// attempt keeps its status when its adapter is no longer enabled, but no launcher is offered for it.
/// </summary>
public sealed class CheckoutPaymentStatusReader(ICheckoutOrderStore orders, IPaymentStore store,
    IEntitlementSynchronizationReader synchronization, IPaymentGatewayResolver gateways, TimeProvider clock,
    IPaymentProviderSelection selection, IClosureStore closures, Changes.ICapacityIncreaseStore increases,
    Changes.IScheduledCapacityReductionStore reductions)
{
    internal async Task<bool> MayAccessOrderAsync(Guid account, Guid order, Guid actor, CancellationToken ct)
    {
        var retained = await orders.FindAsync(account, order, false, ct);
        if (retained?.Quote.Increase is not null && !await orders.IsIncreaseOwnerAsync(account, order, actor, ct))
            return false;
        return retained?.Quote.Reduction is null || await orders.IsReductionOwnerAsync(account, order, actor, ct);
    }

    public async Task<ErrorOr<CheckoutPaymentV1>> ReadAsync(Guid account, Guid order, CancellationToken ct)
    {
        if (await orders.FindAsync(account, order, false, ct) is not { Quote: var quote })
            return CheckoutOrderErrors.Missing;
        var row = await store.ReadStatusAsync(account, order, ct);
        var shops = row.Deliveries.Select(delivery => delivery.ShopId).ToArray();
        var synced = shops.Length == 0 ? new Dictionary<Guid, EntitlementSynchronizationV1>()
            : await synchronization.ReadAsync(account, shops, ct);
        var gateway = row.Attempt is { } attempt ? gateways.ForPin(attempt.Pin) : null;
        var canStart = row.Attempt is null && row.ManualVerifiedAt is null && row.TermId is null &&
            await CanStartAsync(quote, ct) && !await store.HasOverlappingPaymentAsync(account, order, ct);
        var closureStatus = shops.Length == 0 ? [] : await closures.ReadCheckoutClosuresAsync(account, shops, ct);
        var status = Project(quote, row, synced, closureStatus.ToDictionary(s => s.ShopId), canStart, gateway);
        if (quote.Reduction is not null && row.TermId is null && await reductions.OrderStateAsync(account, order, ct) is { } rState)
            return rState is "Funded" or "Applying"
                ? status with { Fulfilment = "Pending", CanStart = false, Launch = null }
                : status with { Fulfilment = "NeedsReview", CanStart = false, Launch = null };
        if (quote.Increase is null || await increases.StateAsync(account, order, ct) is not { } state)
            return status;
        return state == "Applied"
            ? status with { Fulfilment = "Fulfilled", Access = "Applied", TermId = quote.TermId, TermStartsAt = quote.PreviewStartsAt,
                TermEndsAt = quote.PreviewEndsAt, ShopsApplied = quote.ShopCount, CanStart = false, Launch = null }
            : status with { Fulfilment = state, CanStart = false, Launch = null };
    }

    internal async Task<bool> CanStartAsync(CheckoutQuoteV1 quote, CancellationToken ct)
    {
        var now = BillingAccountRules.StorageInstant(clock.GetUtcNow());
        var selected = await selection.SelectedAsync(false, ct);
        if (selected.Pin is not { } pin || gateways.ForPin(pin) is not { } gateway ||
            !gateway.SupportsAmount(quote.AmountPaise, quote.Currency)) return false;
        if (quote.Increase is { } change && (change.RoutingRevision != selected.Revision ||
            change.PinHash != Subscriptions.ConvertCapacityQuoteHandler.PinHash(pin) ||
            !await orders.IncreaseStillCurrentAsync(quote, now, ct))) return false;
        if (quote.Reduction is not null && !await orders.ReductionOrderCurrentAsync(quote, now, ct)) return false;
        return Startable(quote) && now < quote.ExpiresAt && CheckoutRenewalPolicy.WindowOpen(quote, now);
    }

    internal static bool Startable(CheckoutQuoteV1 quote) => CheckoutRenewalPolicy.Payable(quote);

    private static CheckoutPaymentV1 Project(CheckoutQuoteV1 quote, PaymentStatusRow row,
        IReadOnlyDictionary<Guid, EntitlementSynchronizationV1> synced,
        IReadOnlyDictionary<Guid, CheckoutClosureStatus> closures, bool canStart, IPaymentGateway? gateway)
    {
        var attempt = row.Attempt;
        var payment = attempt is null ? (row.ManualVerifiedAt is null ? "NotStarted" : "Verified") : row.VerifiedAt is not null ? "Verified"
            : row.DefinitivelyFailed ? "Failed" : attempt.ProviderOrderReference is null || row.VerificationRequested ? "PendingVerification" : "AwaitingPayment";
        var routingReview = attempt is not null && gateway is null && row.VerifiedAt is null && row.ManualVerifiedAt is null && !row.DefinitivelyFailed;
        if (routingReview) payment = "PendingVerification";
        var fulfilment = row.TermId is not null ? "Fulfilled" : (row.NeedsReview || routingReview) ? "NeedsReview" : "NotFulfilled";
        var applied = row.Deliveries.Count(d => closures.GetValueOrDefault(d.ShopId)?.Closed != true &&
            synced.TryGetValue(d.ShopId, out var s) && Applied(d.ShopRevision, s, closures.GetValueOrDefault(d.ShopId)));
        var review = closures.Values.Any(s => s.Closed) || row.Deliveries.Any(d => d.ShopRevision is not null &&
            synced.TryGetValue(d.ShopId, out var s) && s.State == "NeedsReview" && !Applied(d.ShopRevision, s, closures.GetValueOrDefault(d.ShopId)));
        var access = row.TermId is null ? "NotApplicable"
            : applied == quote.ShopCount ? "Applied" : review ? "NeedsReview" : "Applying";
        // A persisted launcher is offered only while the runtime still permits the attempt's pinned adapter.
        var launch = payment == "AwaitingPayment" && row.ManualVerifiedAt is null && gateway is not null &&
            attempt is { LauncherFlow: { } flow, ProviderOrderReference: { } reference }
            ? new CheckoutLaunchV1(flow, gateway.LauncherParameters(reference, attempt.AmountPaise, attempt.Currency)) : null;
        return new(quote.AccountId, quote.OrderId, attempt?.AttemptId, payment, fulfilment, access, quote.AmountPaise,
            quote.Currency, attempt?.CreatedAt, attempt is null ? row.ManualVerifiedAt : row.VerifiedAt, row.TermId, row.TermStartsAt, row.TermEndsAt,
            quote.ShopCount, applied, canStart, launch, row.ManualVerifiedAt is { } verified ? new("Verified", verified) : null,
            row.RestorationHeld, row.ServiceRestoration);
    }

    private static bool Applied(long? fulfilmentRevision, EntitlementSynchronizationV1 synchronization,
        CheckoutClosureStatus? closure) =>
        fulfilmentRevision is { } revision && synchronization.AppliedShopRevision >= revision &&
        (closure?.ReopeningRevision is null || closure.ReopeningAcknowledged);
}
