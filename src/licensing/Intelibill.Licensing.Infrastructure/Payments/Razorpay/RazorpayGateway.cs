using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Infrastructure.Payments.Razorpay;

/// <summary>
/// Razorpay adapter: one server-created order per attempt with partial payment disabled, and funds proven only by an
/// authenticated read of the order and its payments. A browser callback is at most an unverified hint. Refunds and
/// webhooks are not part of this adapter yet and answer <see cref="GatewayOutcome.Unsupported"/>.
/// </summary>
internal sealed partial class RazorpayGateway(HttpClient http, RazorpayConfiguration config) : IPaymentGateway
{
    internal const string ClientName = "razorpay-gateway";

    public PaymentProviderPin Pin => config.Pin;
    public PaymentGatewayCapabilities Capabilities { get; } = new(FullPaymentOnly: true, Refunds: false, Webhooks: false);

    public IReadOnlyDictionary<string, string> LauncherParameters(string providerOrderReference, long amountPaise, string currency) =>
        new Dictionary<string, string>
        {
            ["keyId"] = config.KeyId, ["orderId"] = providerOrderReference,
            ["amount"] = amountPaise.ToString(System.Globalization.CultureInfo.InvariantCulture), ["currency"] = currency,
            // The pinned allowlist, applied by the launcher's documented display configuration before collection.
            ["methods"] = string.Join(',', RazorpayConfiguration.Supported.Where(config.Methods.Contains)),
        };

    public bool SupportsAmount(long amountPaise, string currency) =>
        currency == "INR" && amountPaise >= RazorpayConfiguration.MinAmountPaise && amountPaise <= config.MaxAmountPaise;

    public async Task<GatewayResult<CheckoutSession>> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct)
    {
        if (!SupportsAmount(request.AmountPaise, request.Currency))
            return GatewayResult.Unsupported<CheckoutSession>();
        var body = JsonSerializer.Serialize(new
        {
            amount = request.AmountPaise, currency = request.Currency, receipt = RazorpayWire.Receipt(request.AttemptId),
            partial_payment = false,
            notes = new { account_id = request.AccountId, order_id = request.OrderId, attempt_id = request.AttemptId },
        });
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/orders")
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        var read = await SendAsync(message, ct);
        if (read.Failure is { } failure)
            return new(failure, null);
        return RazorpayWire.Order(read.Root) is { } order && Owned(order, request) && order.Status == "created" && !order.PartialPayment
            ? GatewayResult.Success(Session(order.Id)) : GatewayResult.Rejected<CheckoutSession>();
    }

    /// <summary>Finds the one order this attempt created, by its receipt. Zero or several matches prove nothing.</summary>
    public async Task<GatewayResult<CheckoutLookup>> LookupCheckoutAsync(CheckoutRequest request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, $"v1/orders?receipt={RazorpayWire.Receipt(request.AttemptId)}&count=2");
        var read = await SendAsync(message, ct);
        if (read.Failure is { } failure)
            return new(failure, null);
        if (RazorpayWire.Items(read.Root) is not { } items)
            return GatewayResult.Rejected<CheckoutLookup>();
        if (items.Count == 0)
            return new(GatewayOutcome.NotFound, null);
        if (items is not [var only] || RazorpayWire.Order(only) is not { } order || order.Receipt != RazorpayWire.Receipt(request.AttemptId) ||
            !Owned(order, request with { AmountPaise = order.Amount, Currency = order.Currency }) || order.PartialPayment)
            return GatewayResult.Rejected<CheckoutLookup>();
        return GatewayResult.Success(new CheckoutLookup(Pin, request with { AmountPaise = order.Amount, Currency = order.Currency },
            Session(order.Id)));
    }

    public async Task<GatewayResult<PaymentObservation>> VerifyCheckoutAsync(CheckoutVerification request, CancellationToken ct)
    {
        if (request.LauncherProof is { } proof && !ProofMatches(proof, request.ProviderOrderReference))
            return GatewayResult.Rejected<PaymentObservation>();
        var order = await ReadOrderAsync(request, ct);
        if (order.Failure is { } failure)
            return new(failure, null);
        var payments = await ReadOrderPaymentsAsync(request.ProviderOrderReference, ct);
        if (payments.Outcome != GatewayOutcome.Succeeded)
            return new(payments.Outcome, null);
        var captured = payments.Value!.Where(p => p.Captured || p.Status is "captured" or "refunded").ToList();
        if (captured.Count == 0)
            return Uncaptured(request, order.Order!, payments.Value!);
        if (captured is not [var entry])
            return GatewayResult.Rejected<PaymentObservation>();
        // The independent read of the payment itself must agree with the collection entry before funds are trusted.
        var single = await ReadPaymentAsync(entry.Id, ct);
        if (single.Failure is { } unavailable)
            return new(unavailable, null);
        return single.Payment is { } entity && entity.OrderId == request.ProviderOrderReference &&
            RazorpayWire.FullCapture(entity, entry, order.Order!) && config.Methods.Contains(entity.Method ?? "")
            ? GatewayResult.Success(RazorpayWire.Observe(Pin, entity)) : GatewayResult.Rejected<PaymentObservation>();
    }

    /// <summary>
    /// No capture in the collection. An order that nevertheless reports funds contradicts it and is rejected; an
    /// authorization is reported as such; otherwise the order is pending, and confirmed unpaid only when provable.
    /// </summary>
    private GatewayResult<PaymentObservation> Uncaptured(CheckoutVerification request, WireOrder order,
        IReadOnlyList<WirePayment> payments)
    {
        if (RazorpayWire.ClaimsFunds(order))
            return GatewayResult.Rejected<PaymentObservation>();
        if (payments.FirstOrDefault(p => p.Status == "authorized") is { } authorized)
            return GatewayResult.Success(RazorpayWire.Observe(Pin, authorized));
        var unpaid = RazorpayWire.ConfirmedUnpaid(order, payments);
        return GatewayResult.Success(new PaymentObservation(Pin, request.ProviderOrderReference, null, ObservedPaymentState.Pending,
            request.ExpectedAmountPaise, request.ExpectedCurrency, null, ConfirmedUnpaid: unpaid, FundingAmbiguous: !unpaid));
    }

    public async Task<GatewayResult<PaymentObservation>> GetPaymentAsync(string providerPaymentReference, CancellationToken ct)
    {
        var read = await ReadPaymentAsync(providerPaymentReference, ct);
        return read.Failure is { } failure ? new(failure, null)
            : read.Payment is { } payment ? GatewayResult.Success(RazorpayWire.Observe(Pin, payment))
            : GatewayResult.Rejected<PaymentObservation>();
    }

    private async Task<(GatewayOutcome? Failure, WirePayment? Payment)> ReadPaymentAsync(string reference, CancellationToken ct)
    {
        if (!SafeReference(reference, "pay_"))
            return (GatewayOutcome.Rejected, null);
        using var message = new HttpRequestMessage(HttpMethod.Get, $"v1/payments/{reference}");
        var read = await SendAsync(message, ct);
        if (read.Failure is { } failure)
            return (failure, null);
        return RazorpayWire.Payment(read.Root) is { } payment && payment.Id == reference
            ? (null, payment) : (GatewayOutcome.Rejected, null);
    }

    public async Task<GatewayResult<IReadOnlyList<PaymentObservation>>> GetOrderPaymentsAsync(string providerOrderReference,
        CancellationToken ct)
    {
        var payments = await ReadOrderPaymentsAsync(providerOrderReference, ct);
        return payments.Outcome == GatewayOutcome.Succeeded
            ? GatewayResult.Success<IReadOnlyList<PaymentObservation>>([.. payments.Value!.Select(p => RazorpayWire.Observe(Pin, p))])
            : new(payments.Outcome, null);
    }

    public Task<GatewayResult<RefundObservation>> RequestRefundAsync(RefundRequest request, CancellationToken ct) =>
        Task.FromResult(GatewayResult.Unsupported<RefundObservation>());

    public Task<GatewayResult<RefundObservation>> GetRefundAsync(string providerRefundReference, CancellationToken ct) =>
        Task.FromResult(GatewayResult.Unsupported<RefundObservation>());

    public GatewayResult<VerifiedGatewayEvent> ValidateWebhook(ReadOnlySpan<byte> body, IReadOnlyDictionary<string, string> headers) =>
        GatewayResult.Unsupported<VerifiedGatewayEvent>();

    private async Task<(GatewayOutcome? Failure, WireOrder? Order)> ReadOrderAsync(CheckoutVerification request, CancellationToken ct)
    {
        if (!SafeReference(request.ProviderOrderReference, "order_"))
            return (GatewayOutcome.Rejected, null);
        using var message = new HttpRequestMessage(HttpMethod.Get, $"v1/orders/{request.ProviderOrderReference}");
        var read = await SendAsync(message, ct);
        if (read.Failure is { } failure)
            return (failure, null);
        var context = request.Context;
        return RazorpayWire.Order(read.Root) is { } order && order.Id == request.ProviderOrderReference &&
            order.Amount == request.ExpectedAmountPaise && order.Currency == request.ExpectedCurrency &&
            (context is null || Owned(order, context)) && !order.PartialPayment
            ? (null, order) : (GatewayOutcome.Rejected, null);
    }

    private async Task<GatewayResult<IReadOnlyList<WirePayment>>> ReadOrderPaymentsAsync(string orderReference, CancellationToken ct)
    {
        if (!SafeReference(orderReference, "order_"))
            return GatewayResult.Rejected<IReadOnlyList<WirePayment>>();
        using var message = new HttpRequestMessage(HttpMethod.Get, $"v1/orders/{orderReference}/payments");
        var read = await SendAsync(message, ct);
        if (read.Failure is { } failure)
            return new(failure, null);
        if (RazorpayWire.Items(read.Root) is not { } items)
            return GatewayResult.Rejected<IReadOnlyList<WirePayment>>();
        var parsed = items.Select(RazorpayWire.Payment).ToList();
        return parsed.Any(p => p is null || p.OrderId != orderReference)
            ? GatewayResult.Rejected<IReadOnlyList<WirePayment>>()
            : GatewayResult.Success<IReadOnlyList<WirePayment>>([.. parsed.Select(p => p!)]);
    }

    private bool ProofMatches(IReadOnlyDictionary<string, string> proof, string serverOrderReference)
    {
        if (!proof.TryGetValue("razorpay_payment_id", out var payment) || !proof.TryGetValue("razorpay_order_id", out var order) ||
            !proof.TryGetValue("razorpay_signature", out var signature) || order != serverOrderReference ||
            !SafeReference(payment, "pay_"))
            return false;
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(config.KeySecret), Encoding.UTF8.GetBytes($"{serverOrderReference}|{payment}"));
        return signature.Length == 64 && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Convert.ToHexStringLower(expected)), Encoding.ASCII.GetBytes(signature.ToLowerInvariant()));
    }

    private static bool Owned(WireOrder order, CheckoutRequest request) =>
        order.Amount == request.AmountPaise && order.Currency == request.Currency &&
        order.AccountNote == request.AccountId.ToString() && order.OrderNote == request.OrderId.ToString() &&
        order.AttemptNote == request.AttemptId.ToString() && order.Receipt == RazorpayWire.Receipt(request.AttemptId);

    private static bool SafeReference(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.Ordinal) && value.Length <= 120 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private static CheckoutSession Session(string order) => new(order, CheckoutPaymentsV1.RazorpayFlow, new Dictionary<string, string>());
}
