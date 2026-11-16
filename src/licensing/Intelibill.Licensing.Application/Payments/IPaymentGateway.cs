namespace Intelibill.Licensing.Application.Payments;

/// <summary>
/// Provider-neutral gateway port. Adapters own provider SDKs, statuses and identifiers; licensing sees only the
/// neutral observations below. Unsupported behaviour answers <see cref="GatewayOutcome.Unsupported"/> rather than
/// approximating it. Callers never hold a SQL transaction across any of these calls.
/// </summary>
public interface IPaymentGateway
{
    PaymentProviderPin Pin { get; }
    PaymentGatewayCapabilities Capabilities { get; }
    Task<GatewayResult<CheckoutSession>> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct);
    Task<GatewayResult<CheckoutLookup>> LookupCheckoutAsync(CheckoutRequest request, CancellationToken ct) =>
        Task.FromResult(GatewayResult.Unsupported<CheckoutLookup>());
    Task<GatewayResult<PaymentObservation>> VerifyCheckoutAsync(CheckoutVerification request, CancellationToken ct);
    Task<GatewayResult<PaymentObservation>> GetPaymentAsync(string providerPaymentReference, CancellationToken ct);
    Task<GatewayResult<IReadOnlyList<PaymentObservation>>> GetOrderPaymentsAsync(string providerOrderReference, CancellationToken ct);
    Task<GatewayResult<RefundObservation>> RequestRefundAsync(RefundRequest request, CancellationToken ct);
    Task<GatewayResult<RefundObservation>> GetRefundAsync(string providerRefundReference, CancellationToken ct);
    GatewayResult<VerifiedGatewayEvent> ValidateWebhook(ReadOnlySpan<byte> body, IReadOnlyDictionary<string, string> headers);
}

/// <summary>Chooses the adapter for new attempts and resolves the pinned adapter for every older attempt.</summary>
public interface IPaymentGatewayResolver
{
    IPaymentGateway? ForNewAttempts();
    IPaymentGateway? ForPin(PaymentProviderPin pin);
}

/// <summary>Local-only capture control; registered only by the development simulator runtime.</summary>
public interface IPaymentSimulator
{
    Task SimulateAsync(string providerOrderReference, string outcome, CancellationToken ct);
}
