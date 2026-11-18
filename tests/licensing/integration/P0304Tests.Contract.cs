using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>The same neutral adapter expectations, run against the simulator and the Razorpay fixture transport.</summary>
public sealed partial class P0304Tests
{
    public static TheoryData<string> Providers => new(Array.Empty<string>()) { "simulator", "razorpay" };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task ContractCapturedFullPaymentFulfilsOnceAcrossReplayAndRaces(string provider)
    {
        await using var run = await ContractRun.StartAsync(database, provider);
        await run.CaptureAsync();

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => run.VerifyAsync(null, null)));
        AssertStatus(await run.VerifyAsync("Success", null), "Verified", "Fulfilled", "Applying");
        await P0302Tests.AssertSingleFulfilmentAsync(run.Fixture, run.Quote, 1);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task ContractCancellationAndMalformedProofGrantNothing(string provider)
    {
        await using var run = await ContractRun.StartAsync(database, provider);
        var malformed = new Dictionary<string, string> { ["razorpay_signature"] = "not-a-signature" };

        AssertStatus(await run.VerifyAsync("Cancelled", null), "PendingVerification", "NotFulfilled", "NotApplicable");
        AssertStatus(await run.VerifyAsync("Success", malformed), "PendingVerification", "NotFulfilled", "NotApplicable");
        Assert.Equal(0L, await run.Fixture.CountAsync("verified_payments"));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task ContractPendingCaptureGrantsNothingUntilTheFullCaptureIsObserved(string provider)
    {
        await using var run = await ContractRun.StartAsync(database, provider);
        await run.PendAsync();

        AssertStatus(await run.VerifyAsync("Success", null), "PendingVerification", "NotFulfilled", "NotApplicable");
        Assert.Equal(0L, await run.Fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await run.Fixture.CountAsync("payment_fulfilments"));

        await run.CaptureAsync();
        AssertStatus(await run.VerifyAsync(null, null), "Verified", "Fulfilled", "Applying");
        await P0302Tests.AssertSingleFulfilmentAsync(run.Fixture, run.Quote, 1);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task ContractWrongAmountCaptureGrantsNothing(string provider)
    {
        await using var run = await ContractRun.StartAsync(database, provider);
        await run.CaptureWrongAmountAsync();

        Assert.NotEqual("Fulfilled", (await run.VerifyAsync(null, null)).GetProperty("fulfilment").GetString());
        Assert.Equal(0L, await run.Fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await run.Fixture.CountAsync("payment_fulfilments"));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task ContractUnsupportedConfigurationRefusesCheckout(string provider)
    {
        var settings = provider == "razorpay" ? FakeRazorpay.Settings() : new Dictionary<string, string?> { ["Payments:Mode"] = "Off" };
        if (provider == "razorpay")
            settings["Payments:Razorpay:AllowPartialPayment"] = "true";
        await using var fixture = await P0302Fixture.StartAsync(database, new P0201Clock { Now = P0210Fixture.Start }, settings,
            services => services.AddHttpClient("razorpay-gateway").ConfigurePrimaryHttpMessageHandler(() => new FakeRazorpay()));
        var (payer, _, _, quote) = await fixture.AcceptedQuoteAsync(1);

        using var response = await payer.Client.PostAsJsonAsync(AttemptsPath(quote), Start(quote));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private sealed class ContractRun : IAsyncDisposable
    {
        private readonly Rig? razorpay;
        private Guid attempt;
        private readonly P0302Fixture? simulator;

        private ContractRun(Rig? razorpay, P0302Fixture? simulator, Merchant payer, Intelibill.Shared.Contracts.V1.CheckoutQuoteV1 quote)
            => (this.razorpay, this.simulator, Payer, Quote) = (razorpay, simulator, payer, quote);

        internal Merchant Payer { get; }
        internal Guid Attempt => attempt;
        internal FakeRazorpay? Fake => razorpay?.Fake;
        internal string? CapturedPayment { get; private set; }
        internal Intelibill.Shared.Contracts.V1.CheckoutQuoteV1 Quote { get; }
        internal P0302Fixture Fixture => razorpay?.Fixture ?? simulator!;

        internal static async Task<ContractRun> StartAsync(LicensingPostgreSqlFixture database, string provider)
        {
            ContractRun run;
            if (provider == "razorpay")
            {
                var rig = await Rig.StartAsync(database);
                run = new(rig, null, rig.Payer, rig.Quote);
            }
            else
            {
                var fixture = await P0302Fixture.StartAsync(database, new P0201Clock { Now = P0210Fixture.Start });
                var (payer, _, _, quote) = await fixture.AcceptedQuoteAsync(1);
                run = new(null, fixture, payer, quote);
            }
            run.attempt = (await P0302Fixture.StartAsync(run.Payer.Client, run.Quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
            return run;
        }

        internal async Task CaptureAsync()
        {
            if (razorpay is { } rig)
                CapturedPayment = rig.Fake.AddPayment(rig.Fake.OrderId(), "captured");
            else
                await P0302Fixture.CaptureAsync(Payer.Client, Quote, attempt);
        }

        internal async Task PendAsync()
        {
            if (razorpay is { } rig)
                rig.Fake.AddPayment(rig.Fake.OrderId(), "authorized");
            else
                await P0302Fixture.PostAsync(Payer.Client, CapturePath(Quote, attempt), new { outcome = "Delayed" });
        }

        internal async Task CaptureWrongAmountAsync()
        {
            if (razorpay is { } rig)
            {
                rig.Fake.AddPayment(rig.Fake.OrderId(), "captured", amount: Quote.AmountPaise - 1);
                return;
            }
            await LicensingPostgreSqlFixture.ExecuteAsync(simulator!.Hosts.LicensingOwner, """
                SELECT set_config('app.development_gateway', 'on', false);
                INSERT INTO public.development_gateway_payments (provider_payment_reference, provider_order_reference,
                    amount_paise, currency, state, captured_at)
                SELECT 'dev_pay_wrong_amount', provider_order_reference, amount_paise - 1, 'INR', 'Captured', now()
                FROM public.development_gateway_sessions;
                """);
        }

        internal async Task<System.Text.Json.JsonElement> VerifyAsync(string? outcome, Dictionary<string, string>? proof)
        {
            using var response = await Payer.Client.PostAsJsonAsync(VerifyPath(Quote, attempt), new { launcherOutcome = outcome, launcherProof = proof });
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }

        public ValueTask DisposeAsync() => Fixture.DisposeAsync();
    }
}
