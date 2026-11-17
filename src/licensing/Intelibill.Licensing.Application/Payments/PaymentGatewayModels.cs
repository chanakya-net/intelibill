namespace Intelibill.Licensing.Application.Payments;

/// <summary>Provider, receiving account, environment and configuration pinned on every attempt and payment.</summary>
public sealed record PaymentProviderPin(string ProviderId, string ProviderAccountId, string Environment, string ConfigurationVersion);

public sealed record PaymentGatewayCapabilities(bool FullPaymentOnly, bool Refunds, bool Webhooks);

public enum GatewayOutcome { Succeeded, Unsupported, Unavailable, Rejected, NotFound }

public sealed record GatewayResult<T>(GatewayOutcome Outcome, T? Value);

public static class GatewayResult
{
    public static GatewayResult<T> Success<T>(T value) => new(GatewayOutcome.Succeeded, value);
    public static GatewayResult<T> Unsupported<T>() => new(GatewayOutcome.Unsupported, default);
    public static GatewayResult<T> Unavailable<T>() => new(GatewayOutcome.Unavailable, default);
    public static GatewayResult<T> Rejected<T>() => new(GatewayOutcome.Rejected, default);
}

public sealed record CheckoutRequest(Guid AccountId, Guid OrderId, Guid AttemptId, long AmountPaise, string Currency);

/// <summary>Opaque provider order reference plus the neutral launcher flow and its safe public parameters.</summary>
public sealed record CheckoutSession(string ProviderOrderReference, string LauncherFlow,
    IReadOnlyDictionary<string, string> LauncherParameters);

public sealed record CheckoutLookup(PaymentProviderPin Pin, CheckoutRequest Request, CheckoutSession Session);

/// <summary>
/// What the server expects of one provider order. The ownership context lets an adapter prove the remote order was
/// created for this attempt; the optional launcher proof is the browser's unverified claim.
/// <see cref="SuccessClaimed"/> marks a browser report of success, which an adapter whose launcher signs its
/// callback refuses without that proof; a proof-free recheck never sets it.
/// </summary>
public sealed record CheckoutVerification(string ProviderOrderReference, long ExpectedAmountPaise, string ExpectedCurrency,
    CheckoutRequest? Context = null, IReadOnlyDictionary<string, string>? LauncherProof = null, bool SuccessClaimed = false);

public enum ObservedPaymentState { Pending, Authorized, Captured, Failed }

/// <summary>
/// A server-side provider observation. Only <see cref="ObservedPaymentState.Captured"/> can fund an order.
/// <see cref="CapturedAt"/> is set only when the provider proves the capture instant; a provider that exposes only the
/// payment's creation time reports it as <see cref="CreatedAt"/> and leaves the capture instant unproven.
/// <see cref="ConfirmedUnpaid"/> is set on a pending observation only when a fresh authenticated read proves the
/// original order open, unpaid and free of any non-failed payment, so the same order may safely be offered again.
/// <see cref="FundingAmbiguous"/> marks a pending observation that is not idle: a payment may be in flight or the
/// provider could not prove the order open and unpaid, so checkout must stop being offered until it is resolved.
/// </summary>
public sealed record PaymentObservation(PaymentProviderPin Pin, string ProviderOrderReference, string? ProviderPaymentReference,
    ObservedPaymentState State, long AmountPaise, string Currency, DateTimeOffset? CapturedAt,
    bool CheckoutDefinitivelyFailed = false, DateTimeOffset? CreatedAt = null, bool ConfirmedUnpaid = false,
    bool FundingAmbiguous = false);

public sealed record RefundRequest(PaymentProviderPin Pin, string ProviderPaymentReference, long AmountPaise, string Currency, Guid RefundId);

public enum ObservedRefundState { Pending, Processed, Failed }

public sealed record RefundObservation(PaymentProviderPin Pin, string ProviderRefundReference, ObservedRefundState State,
    long AmountPaise, string Currency);

public sealed record VerifiedGatewayEvent(PaymentProviderPin Pin, string ProviderEventId, string Kind, PaymentObservation? Payment);
