using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;
using static Intelibill.Licensing.Integration.Tests.P0304Tests;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>Razorpay evidence that contradicts one exact full capture, unsupported methods, amounts and oversize bodies.</summary>
public sealed class P0304TestsEvidence(LicensingPostgreSqlFixture database)
    : IClassFixture<LicensingPostgreSqlFixture>
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
    public async Task DocumentedShapeWithoutAmountCapturedOrPartialPaymentStillFulfils()
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        rig.Fake.AddPayment(order, "captured", withAmountCaptured: false);
        rig.Fake.Order(order).Remove("partial_payment");

        AssertStatus(await rig.VerifyAsync(attempt, null, null), "Verified", "Fulfilled", "Applying");
        await P0302Tests.AssertSingleFulfilmentAsync(rig.Fixture, rig.Quote, 1);
        Assert.Equal(1L, await rig.Fixture.CountAsync("verified_payments", "capture_time_basis = 'ObservedAtVerification'"));
    }

    /// <summary>
    /// Each read states <c>amount_captured</c> only optionally. One read omitting it while the other states the full
    /// amount is still one full capture; any read stating less is not, whichever side states it.
    /// </summary>
    [Theory]
    [InlineData("collection-omits", true)]
    [InlineData("entity-omits", true)]
    [InlineData("collection-short", false)]
    [InlineData("entity-short", false)]
    public async Task AmountCapturedIsJudgedOnEachReadIndependently(string variant, bool fulfils)
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        var payment = rig.Fake.AddPayment(order, "captured");
        var full = rig.Quote.AmountPaise;
        switch (variant)
        {
            case "collection-omits": rig.Fake.OmitFromCollectionEntry(payment, "amount_captured"); break;
            case "entity-omits":
                rig.Fake.Payment(payment).Remove("amount_captured");
                rig.Fake.PatchCollectionEntry(payment, "amount_captured", full);
                break;
            case "collection-short": rig.Fake.PatchCollectionEntry(payment, "amount_captured", full - 1); break;
            default:
                rig.Fake.Payment(payment)["amount_captured"] = full - 1;
                rig.Fake.PatchCollectionEntry(payment, "amount_captured", full);
                break;
        }

        var status = await rig.VerifyAsync(attempt, null, null);
        if (fulfils)
        {
            AssertStatus(status, "Verified", "Fulfilled", "Applying");
            await P0302Tests.AssertSingleFulfilmentAsync(rig.Fixture, rig.Quote, 1);
            return;
        }
        AssertStatus(status, "PendingVerification", "NotFulfilled", "NotApplicable");
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await rig.Fixture.CountAsync("payment_fulfilments"));
        Assert.Equal(0L, await rig.Fixture.CountAsync("subscription_coverage_terms", "mode IS NOT NULL AND true"));
    }

    /// <summary>
    /// The order, the order's payment collection and the payment's own read must each state one exact, unrefunded
    /// capture; anything absent, null or disagreeing is held for reconciliation and grants no term.
    /// </summary>
    [Theory]
    [InlineData("order-attempted")]
    [InlineData("order-created")]
    [InlineData("order-amount-paid-missing")]
    [InlineData("order-amount-paid-null")]
    [InlineData("order-amount-due-missing")]
    [InlineData("order-amount-due-null")]
    [InlineData("payment-amount-refunded-missing")]
    [InlineData("payment-amount-refunded-null")]
    [InlineData("collection-amount-differs")]
    [InlineData("collection-currency-differs")]
    [InlineData("collection-method-differs")]
    [InlineData("collection-refunded")]
    [InlineData("collection-created-at-differs")]
    [InlineData("entity-authorized-only")]
    [InlineData("two-captured-candidates")]
    public async Task InconsistentOrAbsentSettlementEvidenceGrantsNothing(string defect)
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        var payment = rig.Fake.AddPayment(order, "captured");
        Corrupt(rig.Fake, order, payment, defect, rig.Quote.AmountPaise);

        var status = await rig.VerifyAsync(attempt, null, null);
        AssertStatus(status, "PendingVerification", "NotFulfilled", "NotApplicable");
        Assert.Equal(JsonValueKind.Null, status.GetProperty("launch").ValueKind);
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await rig.Fixture.CountAsync("payment_fulfilments"));
        Assert.Equal(0L, await rig.Fixture.CountAsync("subscription_coverage_terms", "mode IS NOT NULL AND true"));
    }

    private static void Corrupt(FakeRazorpay fake, string order, string payment, string defect, long full)
    {
        var entity = fake.Order(order);
        switch (defect)
        {
            case "order-attempted": entity["status"] = "attempted"; break;
            case "order-created": entity["status"] = "created"; break;
            case "order-amount-paid-missing": entity.Remove("amount_paid"); break;
            case "order-amount-paid-null": entity["amount_paid"] = null; break;
            case "order-amount-due-missing": entity.Remove("amount_due"); break;
            case "order-amount-due-null": entity["amount_due"] = null; break;
            case "payment-amount-refunded-missing": fake.Payment(payment).Remove("amount_refunded"); break;
            case "payment-amount-refunded-null": fake.Payment(payment)["amount_refunded"] = null; break;
            case "collection-amount-differs": fake.PatchCollectionEntry(payment, "amount", full + 100); break;
            case "collection-currency-differs": fake.PatchCollectionEntry(payment, "currency", "USD"); break;
            case "collection-method-differs": fake.PatchCollectionEntry(payment, "method", "card"); break;
            case "collection-refunded": fake.PatchCollectionEntry(payment, "amount_refunded", 100); break;
            case "collection-created-at-differs": fake.PatchCollectionEntry(payment, "created_at", 1); break;
            case "entity-authorized-only":
                (fake.Payment(payment)["status"], fake.Payment(payment)["captured"]) = ("authorized", false);
                fake.Payment(payment).Remove("amount_captured");
                break;
            default: fake.AddPayment(order, "captured", method: "card"); break;
        }
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
