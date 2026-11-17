using ErrorOr;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Application.Status;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

/// <summary>
/// Projects the neutral payment, fulfilment and access states of one order. Access is applied for a shop only once
/// the merchant's signed acknowledgement confirms the snapshot published by the fulfilment (or a later one). An
/// attempt keeps its status when its adapter is no longer enabled, but no launcher is offered for it.
/// </summary>
public sealed class CheckoutPaymentStatusReader(ICheckoutOrderStore orders, IPaymentStore store,
    IEntitlementSynchronizationReader synchronization, IPaymentGatewayResolver gateways, TimeProvider clock)
{
    public async Task<ErrorOr<CheckoutPaymentV1>> ReadAsync(Guid account, Guid order, CancellationToken ct)
    {
        if (await orders.FindAsync(account, order, false, ct) is not { Quote: var quote })
            return CheckoutOrderErrors.Missing;
        var row = await store.ReadStatusAsync(account, order, ct);
        var shops = row.Deliveries.Select(delivery => delivery.ShopId).ToArray();
        var synced = shops.Length == 0 ? new Dictionary<Guid, EntitlementSynchronizationV1>()
            : await synchronization.ReadAsync(account, shops, ct);
        var launchable = row.Attempt is { } attempt && gateways.ForPin(attempt.Pin) is not null;
        var canStart = row.Attempt is null && row.ManualVerifiedAt is null && row.TermId is null &&
            CanStart(quote) && !await store.HasOverlappingPaymentAsync(account, order, ct);
        return Project(quote, row, synced, canStart, launchable);
    }

    internal bool CanStart(CheckoutQuoteV1 quote)
    {
        var now = BillingAccountRules.StorageInstant(clock.GetUtcNow());
        return Startable(quote) && gateways.ForNewAttempts() is not null && now < quote.ExpiresAt &&
            CheckoutRenewalPolicy.WindowOpen(quote, now);
    }

    internal static bool Startable(CheckoutQuoteV1 quote) => CheckoutRenewalPolicy.Payable(quote);

    private static CheckoutPaymentV1 Project(CheckoutQuoteV1 quote, PaymentStatusRow row,
        IReadOnlyDictionary<Guid, EntitlementSynchronizationV1> synced, bool canStart, bool launchable)
    {
        var attempt = row.Attempt;
        var payment = attempt is null ? (row.ManualVerifiedAt is null ? "NotStarted" : "Verified") : row.VerifiedAt is not null ? "Verified"
            : row.DefinitivelyFailed ? "Failed" : attempt.ProviderOrderReference is null || row.VerificationRequested ? "PendingVerification" : "AwaitingPayment";
        var fulfilment = row.TermId is not null ? "Fulfilled" : row.NeedsReview ? "NeedsReview" : "NotFulfilled";
        var applied = row.Deliveries.Count(d => synced.TryGetValue(d.ShopId, out var s) && s.AppliedShopRevision >= d.ShopRevision);
        var review = row.Deliveries.Any(d => d.ShopRevision is not null && synced.TryGetValue(d.ShopId, out var s) && s.State == "NeedsReview" &&
            !(s.AppliedShopRevision >= d.ShopRevision));
        var access = row.TermId is null ? "NotApplicable"
            : applied == quote.ShopCount ? "Applied" : review ? "NeedsReview" : "Applying";
        // A persisted launcher is offered only while the runtime still permits the attempt's pinned adapter.
        var launch = payment == "AwaitingPayment" && row.ManualVerifiedAt is null && launchable && attempt?.LauncherFlow is { } flow
            ? new CheckoutLaunchV1(flow, new Dictionary<string, string>()) : null;
        return new(quote.AccountId, quote.OrderId, attempt?.AttemptId, payment, fulfilment, access, quote.AmountPaise,
            quote.Currency, attempt?.CreatedAt, attempt is null ? row.ManualVerifiedAt : row.VerifiedAt, row.TermId, row.TermStartsAt, row.TermEndsAt,
            quote.ShopCount, applied, canStart, launch, row.ManualVerifiedAt is { } verified ? new("Verified", verified) : null,
            row.RestorationHeld, row.ServiceRestoration);
    }
}
