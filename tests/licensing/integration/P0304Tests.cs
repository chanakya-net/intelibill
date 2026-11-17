using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>Razorpay behind the neutral checkout adapter, driven through the real API with a fake HTTP transport only.</summary>
[Collection(MerchantBillingHosts.Name)]
public sealed partial class P0304Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task AcceptedCheckoutCreatesOneFullPaymentOrderAndOffersTheNeutralLauncher()
    {
        await using var rig = await Rig.StartAsync(database);
        var started = await StartAsync(rig.Payer.Client, rig.Quote, HttpStatusCode.Created);

        var order = rig.Fake.Order(rig.Fake.OrderId());
        Assert.False(order["partial_payment"]!.GetValue<bool>());
        Assert.Equal(rig.Quote.AmountPaise, order["amount"]!.GetValue<long>());
        Assert.Equal(1, rig.Fake.Posts);
        var launch = started.GetProperty("launch");
        Assert.Equal(CheckoutPaymentsV1.RazorpayFlow, launch.GetProperty("flow").GetString());
        var parameters = launch.GetProperty("parameters");
        Assert.Equal(FakeRazorpay.KeyId, parameters.GetProperty("keyId").GetString());
        Assert.Equal(rig.Fake.OrderId(), parameters.GetProperty("orderId").GetString());
        Assert.DoesNotContain(FakeRazorpay.KeySecret, started.GetRawText(), StringComparison.Ordinal);
        AssertStatus(started, "AwaitingPayment", "NotFulfilled", "NotApplicable");
    }

    [Fact]
    public async Task CapturedFullPaymentIsFulfilledOnceAndApplyingIsVisible()
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var payment = rig.Fake.AddPayment(rig.Fake.OrderId(), "captured");
        var proof = new Dictionary<string, string>
        {
            ["razorpay_payment_id"] = payment, ["razorpay_order_id"] = rig.Fake.OrderId(),
            ["razorpay_signature"] = FakeRazorpay.Sign(rig.Fake.OrderId(), payment),
        };

        AssertStatus(await rig.VerifyAsync(attempt, "Success", proof), "Verified", "Fulfilled", "Applying");
        AssertStatus(await rig.VerifyAsync(attempt, "Success", proof), "Verified", "Fulfilled", "Applying");
        await P0302Tests.AssertSingleFulfilmentAsync(rig.Fixture, rig.Quote, 1);
        Assert.Contains(rig.Fake.Calls, call => call.StartsWith($"GET /v1/payments/{payment}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ForgedCallbackAndAuthorizationAloneGrantNothing()
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        var authorized = rig.Fake.AddPayment(order, "authorized");

        var forged = new Dictionary<string, string>
        {
            ["razorpay_payment_id"] = authorized, ["razorpay_order_id"] = order, ["razorpay_signature"] = new('a', 64),
        };
        AssertStatus(await rig.VerifyAsync(attempt, "Success", forged), "PendingVerification", "NotFulfilled", "NotApplicable");
        var genuine = Tweak(forged, "razorpay_signature", FakeRazorpay.Sign(order, authorized));
        AssertStatus(await rig.VerifyAsync(attempt, "Success", genuine), "PendingVerification", "NotFulfilled", "NotApplicable");
        var elsewhere = Tweak(genuine, "razorpay_order_id", "order_other");
        AssertStatus(await rig.VerifyAsync(attempt, "Success", elsewhere), "PendingVerification", "NotFulfilled", "NotApplicable");
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));

        rig.Fake.AddPayment(order, "captured");
        AssertStatus(await rig.VerifyAsync(attempt, null, null), "Verified", "Fulfilled", "Applying");
        await P0302Tests.AssertSingleFulfilmentAsync(rig.Fixture, rig.Quote, 1);
    }

    [Fact]
    public async Task CancelledCheckoutKeepsTheAttemptOpenWithoutAGrant()
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        rig.Fake.AddPayment(rig.Fake.OrderId(), "failed");

        AssertStatus(await rig.VerifyAsync(attempt, "Cancelled", null), "PendingVerification", "NotFulfilled", "NotApplicable");
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("order")]
    public async Task CapturedFundsThatDoNotMatchTheAcceptedOrderGrantNothing(string mismatch)
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        rig.Fake.AddPayment(order, "captured",
            amount: mismatch == "amount" ? rig.Quote.AmountPaise - 1 : null,
            currency: mismatch == "currency" ? "USD" : null,
            onOrder: mismatch == "order" ? "order_someone_else" : null);

        var status = await rig.VerifyAsync(attempt, null, null);
        Assert.NotEqual("Fulfilled", status.GetProperty("fulfilment").GetString());
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await rig.Fixture.CountAsync("payment_fulfilments"));
    }

    [Fact]
    public async Task ProviderOrderOwnedByAnotherAttemptIsNeverTrusted()
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        rig.Fake.Order(order)["notes"]!["attempt_id"] = Guid.NewGuid().ToString();
        rig.Fake.AddPayment(order, "captured");

        Assert.NotEqual("Fulfilled", (await rig.VerifyAsync(attempt, null, null)).GetProperty("fulfilment").GetString());
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
    }

    [Fact]
    public async Task ProviderRedirectCannotSupplyPaymentEvidence()
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        rig.Fake.AddPayment(rig.Fake.OrderId(), "captured");
        rig.Fake.Redirect = true;
        Assert.NotEqual("Fulfilled", (await rig.VerifyAsync(attempt, null, null)).GetProperty("fulfilment").GetString());
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
    }

    [Fact]
    public async Task ConcurrentAndReplayedVerificationFulfilsAtMostOnce()
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        rig.Fake.AddPayment(rig.Fake.OrderId(), "captured");

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => rig.VerifyAsync(attempt, null, null)));
        AssertStatus(await rig.VerifyAsync(attempt, null, null), "Verified", "Fulfilled", "Applying");
        await P0302Tests.AssertSingleFulfilmentAsync(rig.Fixture, rig.Quote, 1);
        Assert.Equal(1L, await rig.Fixture.CountAsync("verified_payments"));
    }

    [Fact]
    public async Task LostCreateResponseIsRecoveredFromTheOriginalOrderWithoutASecondPost()
    {
        await using var rig = await Rig.StartAsync(database);
        rig.Fake.LoseCreateResponse = true;
        var started = await StartAsync(rig.Payer.Client, rig.Quote, HttpStatusCode.Created);
        var attempt = started.GetProperty("attemptId").GetGuid();
        AssertStatus(started, "PendingVerification", "NotFulfilled", "NotApplicable");
        Assert.Equal(1, rig.Fake.Posts);

        // Check again recovers the unpaid original order; the customer can launch that same order, across a reload.
        rig.Fake.LoseCreateResponse = false;
        var recovered = await rig.VerifyAsync(attempt, null, null);
        AssertStatus(recovered, "AwaitingPayment", "NotFulfilled", "NotApplicable");
        Assert.Equal(rig.Fake.OrderId(), recovered.GetProperty("launch").GetProperty("parameters").GetProperty("orderId").GetString());
        AssertStatus(await StatusAsync(rig.Payer.Client, rig.Quote), "AwaitingPayment", "NotFulfilled", "NotApplicable");
        Assert.Equal(1, rig.Fake.Posts);

        rig.Fake.AddPayment(rig.Fake.OrderId(), "captured");
        AssertStatus(await rig.VerifyAsync(attempt, null, null), "Verified", "Fulfilled", "Applying");
        Assert.Equal(1, rig.Fake.Posts);
        await P0302Tests.AssertSingleFulfilmentAsync(rig.Fixture, rig.Quote, 1);
    }

    [Theory]
    [InlineData("Payments:Razorpay:Environment", "Live")]
    [InlineData("Payments:Razorpay:Methods:1", "emi")]
    [InlineData("Payments:Razorpay:Methods:1", "bank_transfer")]
    [InlineData("Payments:Razorpay:Methods:0", "upi")]
    [InlineData("Payments:Razorpay:MaxAmountPaise", "99")]
    [InlineData("Payments:Razorpay:MaxAmountPaise", "")]
    [InlineData("Payments:Razorpay:AllowPartialPayment", "true")]
    [InlineData("Payments:Razorpay:Currency", "USD")]
    [InlineData("Payments:Razorpay:KeySecret", "")]
    [InlineData("Payments:Razorpay:BaseUrl", "https://attacker.example/")]
    public async Task UnsupportedConfigurationRefusesCheckoutBeforeAnyProviderCall(string key, string value)
    {
        var fake = new FakeRazorpay();
        var settings = FakeRazorpay.Settings();
        settings[key] = value;
        await using var fixture = await P0302Fixture.StartAsync(database, new P0201Clock { Now = P0210Fixture.Start }, settings,
            services => services.AddHttpClient("razorpay-gateway").ConfigurePrimaryHttpMessageHandler(() => fake));
        var (payer, _, _, quote) = await fixture.AcceptedQuoteAsync(1);

        Assert.False((await StatusAsync(payer.Client, quote)).GetProperty("canStart").GetBoolean());
        using var response = await payer.Client.PostAsJsonAsync(AttemptsPath(quote), Start(quote));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task VerifiedFundsStayPinnedToTheConfiguredAccountEnvironmentAndVersion()
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        rig.Fake.AddPayment(rig.Fake.OrderId(), "captured");
        AssertStatus(await rig.VerifyAsync(attempt, null, null), "Verified", "Fulfilled", "Applying");

        const string pin = "provider_id = 'razorpay' AND provider_account_id = 'fixture-account' AND environment = 'Test' " +
            "AND configuration_version = 'fixture/1'";
        Assert.Equal(1L, await rig.Fixture.CountAsync("payment_attempts", pin));
        Assert.Equal(1L, await rig.Fixture.CountAsync("verified_payments", pin));
    }

    private static Dictionary<string, string> Tweak(Dictionary<string, string> proof, string key, string value) =>
        new(proof) { [key] = value };

    internal sealed class Rig : IAsyncDisposable
    {
        internal required P0302Fixture Fixture { get; init; }
        internal required FakeRazorpay Fake { get; init; }
        internal required Merchant Payer { get; init; }
        internal required CheckoutQuoteV1 Quote { get; init; }

        internal static async Task<Rig> StartAsync(LicensingPostgreSqlFixture database, string? account = null,
            long monthlyPricePaise = 12000L, long? maxAmountPaise = null, string[]? methods = null)
        {
            var fake = new FakeRazorpay();
            var clock = new P0201Clock { Now = P0210Fixture.Start };
            fake.Clock = () => clock.Now;
            var settings = FakeRazorpay.Settings();
            if (account is not null)
                settings["Payments:Razorpay:AccountId"] = account;
            if (methods is not null)
            {
                settings.Remove("Payments:Razorpay:Methods:0");
                settings.Remove("Payments:Razorpay:Methods:1");
                for (var index = 0; index < methods.Length; index++)
                    settings[$"Payments:Razorpay:Methods:{index}"] = methods[index];
            }
            if (maxAmountPaise is { } max)
                settings["Payments:Razorpay:MaxAmountPaise"] = max.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var fixture = await P0302Fixture.StartAsync(database, clock, settings,
                services => services.AddHttpClient("razorpay-gateway").ConfigurePrimaryHttpMessageHandler(() => fake));
            var (payer, _, _, quote) = await fixture.AcceptedQuoteAsync(1, monthlyPricePaise: monthlyPricePaise);
            return new() { Fixture = fixture, Fake = fake, Payer = payer, Quote = quote };
        }

        internal async Task<Guid> StartAttemptAsync() =>
            (await P0302Fixture.StartAsync(Payer.Client, Quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();

        internal async Task<JsonElement> VerifyAsync(Guid attempt, string? outcome, Dictionary<string, string>? proof)
        {
            using var response = await Payer.Client.PostAsJsonAsync(VerifyPath(Quote, attempt),
                new { launcherOutcome = outcome, launcherProof = proof });
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }

        public ValueTask DisposeAsync() => Fixture.DisposeAsync();
    }
}
