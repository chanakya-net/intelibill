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
    IPaymentProviderSelection selection, IClosureStore closures)
{
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
        var closed = shops.Length == 0 ? [] : await closures.ClosedShopIdsAsync(account, shops, ct);
        return Project(quote, row, synced, closed, canStart, gateway);
    }

    internal async Task<bool> CanStartAsync(CheckoutQuoteV1 quote, CancellationToken ct)
    {
        var now = BillingAccountRules.StorageInstant(clock.GetUtcNow());
        var selected = await selection.SelectedAsync(false, ct);
        return Startable(quote) && selected.Pin is { } pin && gateways.ForPin(pin) is { } gateway && gateway.SupportsAmount(quote.AmountPaise, quote.Currency) && now < quote.ExpiresAt &&
            CheckoutRenewalPolicy.WindowOpen(quote, now);
    }

    internal static bool Startable(CheckoutQuoteV1 quote) => CheckoutRenewalPolicy.Payable(quote);

    private static CheckoutPaymentV1 Project(CheckoutQuoteV1 quote, PaymentStatusRow row,
        IReadOnlyDictionary<Guid, EntitlementSynchronizationV1> synced, Guid[] closed, bool canStart, IPaymentGateway? gateway)
    {
        var attempt = row.Attempt;
        var payment = attempt is null ? (row.ManualVerifiedAt is null ? "NotStarted" : "Verified") : row.VerifiedAt is not null ? "Verified"
            : row.DefinitivelyFailed ? "Failed" : attempt.ProviderOrderReference is null || row.VerificationRequested ? "PendingVerification" : "AwaitingPayment";
        var routingReview = attempt is not null && gateway is null && row.VerifiedAt is null && row.ManualVerifiedAt is null && !row.DefinitivelyFailed;
        if (routingReview) payment = "PendingVerification";
        var fulfilment = row.TermId is not null ? "Fulfilled" : (row.NeedsReview || routingReview) ? "NeedsReview" : "NotFulfilled";
        var applied = row.Deliveries.Count(d => !closed.Contains(d.ShopId) && synced.TryGetValue(d.ShopId, out var s) && s.AppliedShopRevision >= d.ShopRevision);
        var review = row.Deliveries.Any(d => d.ShopRevision is not null && synced.TryGetValue(d.ShopId, out var s) && s.State == "NeedsReview" &&
            !(s.AppliedShopRevision >= d.ShopRevision));
        var access = row.TermId is null ? "NotApplicable"
            : applied == quote.ShopCount ? "Applied" : review || closed.Length != 0 ? "NeedsReview" : "Applying";
        // A persisted launcher is offered only while the runtime still permits the attempt's pinned adapter.
        var launch = payment == "AwaitingPayment" && row.ManualVerifiedAt is null && gateway is not null &&
            attempt is { LauncherFlow: { } flow, ProviderOrderReference: { } reference }
            ? new CheckoutLaunchV1(flow, gateway.LauncherParameters(reference, attempt.AmountPaise, attempt.Currency)) : null;
        return new(quote.AccountId, quote.OrderId, attempt?.AttemptId, payment, fulfilment, access, quote.AmountPaise,
            quote.Currency, attempt?.CreatedAt, attempt is null ? row.ManualVerifiedAt : row.VerifiedAt, row.TermId, row.TermStartsAt, row.TermEndsAt,
            quote.ShopCount, applied, canStart, launch, row.ManualVerifiedAt is { } verified ? new("Verified", verified) : null,
            row.RestorationHeld, row.ServiceRestoration);
    }
}
