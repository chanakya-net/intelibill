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
internal sealed class RazorpayGateway(HttpClient http, RazorpayConfiguration config) : IPaymentGateway
{
    internal const string ClientName = "razorpay-gateway";
    private const int MaxBodyBytes = 262_144;

    public PaymentProviderPin Pin => config.Pin;
    public PaymentGatewayCapabilities Capabilities { get; } = new(FullPaymentOnly: true, Refunds: false, Webhooks: false);

    public IReadOnlyDictionary<string, string> LauncherParameters(string providerOrderReference, long amountPaise, string currency) =>
        new Dictionary<string, string>
        {
            ["keyId"] = config.KeyId, ["orderId"] = providerOrderReference,
            ["amount"] = amountPaise.ToString(System.Globalization.CultureInfo.InvariantCulture), ["currency"] = currency,
        };

    public async Task<GatewayResult<CheckoutSession>> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct)
    {
        if (request.Currency != "INR" || request.AmountPaise <= 0)
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
        var captured = payments.Value!.Where(p => p.Captured && p.Status == "captured").OrderBy(p => p.CreatedAt).ThenBy(p => p.Id,
            StringComparer.Ordinal).FirstOrDefault();
        if (captured is null)
        {
            var best = payments.Value!.FirstOrDefault(p => p.Status == "authorized");
            return GatewayResult.Success(best is null
                ? new PaymentObservation(Pin, request.ProviderOrderReference, null, ObservedPaymentState.Pending,
                    request.ExpectedAmountPaise, request.ExpectedCurrency, null)
                : RazorpayWire.Observe(Pin, best));
        }
        // The independent read of the payment itself must agree with the collection entry before funds are trusted.
        var single = await GetPaymentAsync(captured.Id, ct);
        return single is { Outcome: GatewayOutcome.Succeeded, Value.ProviderOrderReference: var reference } &&
            reference == request.ProviderOrderReference && single.Value.State == ObservedPaymentState.Captured
            ? single : GatewayResult.Rejected<PaymentObservation>();
    }

    public async Task<GatewayResult<PaymentObservation>> GetPaymentAsync(string providerPaymentReference, CancellationToken ct)
    {
        if (!providerPaymentReference.StartsWith("pay_", StringComparison.Ordinal) || providerPaymentReference.Length > 120 ||
            providerPaymentReference.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            return GatewayResult.Rejected<PaymentObservation>();
        using var message = new HttpRequestMessage(HttpMethod.Get, $"v1/payments/{providerPaymentReference}");
        var read = await SendAsync(message, ct);
        if (read.Failure is { } failure)
            return new(failure, null);
        return RazorpayWire.Payment(read.Root) is { } payment && payment.Id == providerPaymentReference
            ? GatewayResult.Success(RazorpayWire.Observe(Pin, payment)) : GatewayResult.Rejected<PaymentObservation>();
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

    private async Task<(GatewayOutcome? Failure, JsonElement Root)> SendAsync(HttpRequestMessage message, CancellationToken ct)
    {
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.KeyId}:{config.KeySecret}")));
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return (GatewayOutcome.NotFound, default);
        if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
            return (GatewayOutcome.Unavailable, default);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBodyBytes)
            return (GatewayOutcome.Rejected, default);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        try
        {
            return bytes.Length > MaxBodyBytes ? (GatewayOutcome.Rejected, default) : (null, JsonDocument.Parse(bytes).RootElement.Clone());
        }
        catch (JsonException)
        {
            return (GatewayOutcome.Rejected, default);
        }
    }
}
