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
/// </summary>
public sealed record CheckoutVerification(string ProviderOrderReference, long ExpectedAmountPaise, string ExpectedCurrency,
    CheckoutRequest? Context = null, IReadOnlyDictionary<string, string>? LauncherProof = null);

public enum ObservedPaymentState { Pending, Authorized, Captured, Failed }

/// <summary>A server-side provider observation. Only <see cref="ObservedPaymentState.Captured"/> can fund an order.</summary>
public sealed record PaymentObservation(PaymentProviderPin Pin, string ProviderOrderReference, string? ProviderPaymentReference,
    ObservedPaymentState State, long AmountPaise, string Currency, DateTimeOffset? CapturedAt,
    bool CheckoutDefinitivelyFailed = false);

public sealed record RefundRequest(PaymentProviderPin Pin, string ProviderPaymentReference, long AmountPaise, string Currency, Guid RefundId);

public enum ObservedRefundState { Pending, Processed, Failed }

public sealed record RefundObservation(PaymentProviderPin Pin, string ProviderRefundReference, ObservedRefundState State,
    long AmountPaise, string Currency);

public sealed record VerifiedGatewayEvent(PaymentProviderPin Pin, string ProviderEventId, string Kind, PaymentObservation? Payment);
