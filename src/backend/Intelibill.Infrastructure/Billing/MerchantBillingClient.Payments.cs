using System.Net;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.Hosting;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient : IMerchantCheckoutPayments
{
    public Task<ErrorOr<CheckoutPaymentV1>> ReadPaymentAsync(MerchantBillingCaller caller, Guid account, Guid order,
        CancellationToken ct) =>
        PaymentAsync(caller, new(CheckoutPaymentsV1.Read, HttpMethod.Get, CheckoutPaymentsV1.PaymentPath(account, order),
            account, []), account, order, ct);

    public async Task<ErrorOr<MerchantCheckoutPayment>> StartPaymentAsync(MerchantBillingCaller caller, Guid account,
        Guid order, StartCheckoutPaymentV1 request, CancellationToken ct)
    {
        var created = false;
        var result = await RunAsync<CheckoutPaymentV1>(caller, new(CheckoutPaymentsV1.Start, HttpMethod.Post,
            CheckoutPaymentsV1.AttemptsPath(account, order), account, JsonSerializer.SerializeToUtf8Bytes(request, Json)),
            CheckoutPaymentsV1.MaxStatusBytes, async (response, token) =>
            {
                created = response.StatusCode == HttpStatusCode.Created;
                return await ReadPaymentAsync(response, account, order, token);
            }, ct);
        return result.IsError ? result.Errors : new MerchantCheckoutPayment(result.Value, created);
    }

    public Task<ErrorOr<CheckoutPaymentV1>> VerifyPaymentAsync(MerchantBillingCaller caller, Guid account, Guid order,
        Guid attempt, VerifyCheckoutPaymentV1 request, CancellationToken ct) =>
        PaymentAsync(caller, new(CheckoutPaymentsV1.Verify, HttpMethod.Post, CheckoutPaymentsV1.VerifyPath(account, order, attempt),
            account, JsonSerializer.SerializeToUtf8Bytes(request, Json)), account, order, ct);

    public Task<ErrorOr<CheckoutPaymentV1>> SimulateCaptureAsync(MerchantBillingCaller caller, Guid account, Guid order,
        Guid attempt, SimulateCheckoutCaptureV1 request, CancellationToken ct) =>
        PaymentAsync(caller, new(CheckoutPaymentsV1.Simulate, HttpMethod.Post, CheckoutPaymentsV1.CapturePath(account, order, attempt),
            account, JsonSerializer.SerializeToUtf8Bytes(request, Json)), account, order, ct);

    private Task<ErrorOr<CheckoutPaymentV1>> PaymentAsync(MerchantBillingCaller caller, BillingDelegationIntent intent,
        Guid account, Guid order, CancellationToken ct) =>
        RunAsync<CheckoutPaymentV1>(caller, intent, CheckoutPaymentsV1.MaxStatusBytes,
            (response, token) => ReadPaymentAsync(response, account, order, token), ct);

    private async Task<ErrorOr<CheckoutPaymentV1>> ReadPaymentAsync(HttpResponseMessage response, Guid account,
        Guid order, CancellationToken ct)
    {
        var payment = await ReadJsonAsync<CheckoutPaymentV1>(response, ct);
        return payment is not null && RelayPayment(payment, account, order, environment) is { } relayed
            ? relayed
            : Unavailable("InvalidCheckoutPayment");
    }

    /// <summary>
    /// A valid payment keeps its neutral state; its launcher is relayed with only its allow-listed public parameters, and a simulator launcher
    /// only by a Development or Testing merchant host, whatever the licensing service answered.
    /// </summary>
    internal static CheckoutPaymentV1? RelayPayment(CheckoutPaymentV1 payment, Guid account, Guid order,
        IHostEnvironment environment)
    {
        if (!IsPaymentValid(payment, account, order))
            return null;
        var launch = payment.Launch is { } offered &&
            (offered.Flow != CheckoutPaymentsV1.SimulatorFlow || MerchantBillingDelegationOptions.AllowsLocalCredentials(environment))
            ? new CheckoutLaunchV1(offered.Flow, new Dictionary<string, string>(offered.Parameters)) : null;
        return payment with { Launch = launch };
    }

    /// <summary>Only the neutral vocabulary, allow-listed launcher flows and their allow-listed public parameters are relayed.</summary>
    internal static bool IsPaymentValid(CheckoutPaymentV1 p, Guid account, Guid order) =>
        p.SchemaVersion == 1 && p.AccountId == account && p.OrderId == order && p.AttemptId != Guid.Empty &&
        CheckoutPaymentsV1.PaymentStates.Contains(p.Payment) && CheckoutPaymentsV1.FulfilmentStates.Contains(p.Fulfilment) &&
        CheckoutPaymentsV1.AccessStates.Contains(p.Access) && CheckoutPaymentsV1.HasValidPaymentSource(p) &&
        p.AmountPaise is > 0 and <= CheckoutOrdersV1.MaxPaise && p.Currency == "INR" &&
        p.ShopsTotal is > 0 and <= CheckoutOrdersV1.MaxShops && p.ShopsApplied >= 0 && p.ShopsApplied <= p.ShopsTotal &&
        (p.Fulfilment == "Fulfilled") == (p.TermId is not null && p.TermStartsAt < p.TermEndsAt) &&
        (p.Access == "NotApplicable") == (p.Fulfilment != "Fulfilled") &&
        (p.Launch is null || p.Payment == "AwaitingPayment" && CheckoutPaymentsV1.ValidLaunch(p.Launch.Flow, p.Launch.Parameters));

    private Error MapPaymentFailure(HttpStatusCode status, string? code) => (status, code) switch
    {
        (HttpStatusCode.Conflict, CheckoutPaymentsV1.Unavailable) => Error.Conflict(code, "Online payment is not available here."),
        (HttpStatusCode.Conflict, CheckoutPaymentsV1.Unsupported) => Error.Conflict(code, "This order cannot be paid online yet."),
        (HttpStatusCode.Conflict, CheckoutPaymentsV1.NotAccepted) => Error.Conflict(code, "Accept the quote before paying."),
        (HttpStatusCode.Conflict, CheckoutPaymentsV1.AlreadyStarted) => Error.Conflict(code, "Payment for this order already started."),
        (HttpStatusCode.NotFound, CheckoutPaymentsV1.NotFound) => Error.NotFound(code, "The payment attempt was not found."),
        (HttpStatusCode.NotFound, null) => Error.NotFound(CheckoutPaymentsV1.NotFound, "The payment attempt was not found."),
        _ => MapCheckoutFailure(status, code),
    };
}
