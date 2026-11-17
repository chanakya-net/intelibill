using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>Razorpay evidence that contradicts one exact full capture, unsupported methods, amounts and oversize bodies.</summary>
public sealed partial class P0304Tests
{
    [Theory]
    [InlineData("short-capture")]
    [InlineData("order-paid-short")]
    [InlineData("order-due-remaining")]
    [InlineData("order-totals-disagree")]
    [InlineData("refunded-before-first-verification")]
    [InlineData("malformed-partial-payment")]
    [InlineData("malformed-amount-captured")]
    public async Task EvidenceThatContradictsOneFullCaptureGrantsNothing(string defect)
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        var payment = rig.Fake.AddPayment(order, "captured");
        var full = rig.Quote.AmountPaise;
        switch (defect)
        {
            case "short-capture": rig.Fake.Payment(payment)["amount_captured"] = full - 1; break;
            case "order-paid-short": rig.Fake.Order(order)["amount_paid"] = full - 1; break;
            case "order-due-remaining": rig.Fake.Order(order)["amount_due"] = 1; break;
            case "order-totals-disagree": (rig.Fake.Order(order)["amount_paid"], rig.Fake.Order(order)["amount_due"]) = (full, 5); break;
            case "refunded-before-first-verification": rig.Fake.Payment(payment)["amount_refunded"] = 100; break;
            case "malformed-partial-payment": rig.Fake.Order(order)["partial_payment"] = "yes"; break;
            default: rig.Fake.Payment(payment)["amount_captured"] = "all"; break;
        }

        Assert.NotEqual("Fulfilled", (await rig.VerifyAsync(attempt, null, null)).GetProperty("fulfilment").GetString());
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await rig.Fixture.CountAsync("payment_fulfilments"));
        Assert.Equal(0L, await rig.Fixture.CountAsync("subscription_coverage_terms", "mode IS NOT NULL AND true"));
    }

    [Fact]
    public async Task DocumentedShapeWithoutCaptureAndRefundFieldsStillFulfilsFromTheRemainingEvidence()
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        rig.Fake.AddPayment(order, "captured", documentedCaptureFields: false);
        rig.Fake.Order(order).Remove("amount_paid");
        rig.Fake.Order(order).Remove("partial_payment");

        AssertStatus(await rig.VerifyAsync(attempt, null, null), "Verified", "Fulfilled", "Applying");
        await P0302Tests.AssertSingleFulfilmentAsync(rig.Fixture, rig.Quote, 1);
        Assert.Equal(1L, await rig.Fixture.CountAsync("verified_payments", "capture_time_basis = 'ObservedAtVerification'"));
    }

    [Theory]
    [InlineData("emi")]
    [InlineData("paylater")]
    [InlineData("cardless_emi")]
    public async Task CapturedPaymentByAnUnsupportedMethodCreatesNoFulfilment(string method)
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        rig.Fake.AddPayment(rig.Fake.OrderId(), "captured", method: method);

        Assert.NotEqual("Fulfilled", (await rig.VerifyAsync(attempt, null, null)).GetProperty("fulfilment").GetString());
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await rig.Fixture.CountAsync("payment_fulfilments"));
    }

    [Theory]
    [InlineData(99L, 50_000_000L, false)]
    [InlineData(100L, 50_000_000L, true)]
    [InlineData(5000L, 5000L, true)]
    [InlineData(5000L, 4999L, false)]
    public async Task AmountsOutsideTheProvidersRangeAreRefusedBeforeAnAttemptOrProviderCall(long price, long max, bool supported)
    {
        await using var rig = await Rig.StartAsync(database, monthlyPricePaise: price, maxAmountPaise: max);
        Assert.Equal(price, rig.Quote.AmountPaise);
        Assert.Equal(supported, (await StatusAsync(rig.Payer.Client, rig.Quote)).GetProperty("canStart").GetBoolean());

        using var response = await rig.Payer.Client.PostAsJsonAsync(AttemptsPath(rig.Quote), Start(rig.Quote));
        Assert.Equal(supported ? HttpStatusCode.Created : HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(supported ? 1 : 0, rig.Fake.Posts);
        Assert.Equal(supported ? 1L : 0L, await rig.Fixture.CountAsync("payment_attempts"));
    }

    [Fact]
    public async Task LengthlessOversizedProviderResponseStopsAtTheLimitAndRecordsNoPayment()
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        rig.Fake.AddPayment(rig.Fake.OrderId(), "captured");
        rig.Fake.LengthlessOversize = true;

        Assert.NotEqual("Fulfilled", (await rig.VerifyAsync(attempt, null, null)).GetProperty("fulfilment").GetString());
        Assert.InRange(rig.Fake.OversizeBytesServed, 1, 262_144 + 8192);
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
    }

    [Fact]
    public async Task NullLauncherProofValueIsRefusedAsInvalidWithoutAProviderCall()
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var before = rig.Fake.Calls.Count;
        using var body = new StringContent("""{"launcherOutcome":"Success","launcherProof":{"razorpay_signature":null}}""",
            System.Text.Encoding.UTF8, "application/json");
        using var response = await rig.Payer.Client.PostAsync(VerifyPath(rig.Quote, attempt), body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, rig.Fake.Calls.Count);
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
    }

    [Fact]
    public async Task ForgedCallbackAgainstAnAlreadyCapturedSameOrderGrantsNothingAndProofFreeRecoveryVerifiesTheCapture()
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        var payment = rig.Fake.AddPayment(order, "captured");
        var forged = new Dictionary<string, string>
        {
            ["razorpay_payment_id"] = payment, ["razorpay_order_id"] = order, ["razorpay_signature"] = new('b', 64),
        };

        Assert.NotEqual("Fulfilled", (await rig.VerifyAsync(attempt, "Success", forged)).GetProperty("fulfilment").GetString());
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await rig.Fixture.CountAsync("payment_fulfilments"));

        AssertStatus(await rig.VerifyAsync(attempt, null, null), "Verified", "Fulfilled", "Applying");
        await P0302Tests.AssertSingleFulfilmentAsync(rig.Fixture, rig.Quote, 1);
    }

    [Theory]
    [InlineData("Payments:Razorpay:KeySecret", "not-the-account-secret")]
    [InlineData("Payments:Razorpay:KeyId", "rzp_test_otheraccount")]
    public async Task CredentialsThatTheProviderDoesNotRecogniseNeverProduceVerifiedFunds(string key, string value)
    {
        var fake = new FakeRazorpay();
        var settings = FakeRazorpay.Settings();
        settings[key] = value;
        await using var fixture = await P0302Fixture.StartAsync(database, new P0201Clock { Now = P0210Fixture.Start }, settings,
            services => services.AddHttpClient("razorpay-gateway").ConfigurePrimaryHttpMessageHandler(() => fake));
        var (payer, _, _, quote) = await fixture.AcceptedQuoteAsync(1);
        var started = await StartAsync(payer.Client, quote, HttpStatusCode.Created);

        AssertStatus(started, "PendingVerification", "NotFulfilled", "NotApplicable");
        Assert.Equal(0, fake.Posts);
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        Assert.Equal(1L, await fixture.CountAsync("payment_attempts", "configuration_version = 'fixture/1' AND environment = 'Test'"));
    }
}
