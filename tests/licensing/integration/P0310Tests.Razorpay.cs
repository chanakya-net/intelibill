using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Intelibill.Licensing.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0310Tests
{
    [Theory]
    [InlineData("acc_b0310", "Test", "b/1")]
    [InlineData("acc_a0310", "Test", "a/2")]
    [InlineData("acc_a0310", "Live", "a/live-fixture")]
    public async Task RazorpayTransportFixtureKeepsOriginalRecoveryAndFutureRefundContract(string accountB, string environmentB, string versionB)
    {
        var clock = new P0201Clock { Now = WholeSecondStart };
        const string keyA = "rzp_test_a0310";
        var keyB = environmentB == "Test" ? "rzp_test_b0310" : "rzp_live_b0310";
        var first = new FakeRazorpay(keyA, SecretA + "-api") { Clock = () => clock.Now, LoseCreateResponse = true };
        var second = new FakeRazorpay(keyB, SecretB, sequenceStart: 1000) { Clock = () => clock.Now };
        using var transport = new IdentityTransport(first, second, keyA, SecretA + "-api");
        var settings = RazorpayProfiles(accountB, environmentB, versionB, keyA, keyB);
        if (accountB == "acc_a0310" && environmentB == "Test")
            settings["FixtureCredentials:1:RetiredWebhookSecrets:0"] = SecretA;
        await using var fixture = await P0302Fixture.StartAsync(database, clock, settings, services =>
            services.AddHttpClient("razorpay-gateway").ConfigurePrimaryHttpMessageHandler(() => transport));
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        await SelectProfileAsync(owner, "a", 0);
        var old = await fixture.AcceptedQuoteAsync(1);
        var key = Guid.NewGuid();
        var attempt = (await StartAsync(old.Payer.Client, old.Quote, HttpStatusCode.Created, key)).GetProperty("attemptId").GetGuid();
        await SelectProfileAsync(owner, "b", 1);
        var next = await fixture.AcceptedQuoteAsync(1);
        var nextStatus = await StartAsync(next.Payer.Client, next.Quote, HttpStatusCode.Created);
        AssertStatus(nextStatus, "AwaitingPayment", "NotFulfilled", "NotApplicable");
        await AssertMissingRazorpayCredentialAsync(fixture, old, attempt, first, second);
        // Recover the lost session through A without claiming money; only its delayed capture funds the order.
        AssertStatus(await VerifyAsync(old.Payer.Client, old.Quote, attempt), "AwaitingPayment", "NotFulfilled", "NotApplicable");
        var payment = first.AddPayment(first.OrderId(), "captured");
        using var client = fixture.Hosts.Licensing.CreateClient();
        using var received = await P0305Tests.SendAsync(client, P0305Tests.Body(old.Quote.AmountPaise, clock.Now, first.OrderId(), payment,
            account: "acc_a0310"), "p0310_old", SecretA);
        Assert.Equal(HttpStatusCode.NoContent, received.StatusCode);
        if (accountB == "acc_a0310" && environmentB == "Test")
        {
            using var duplicate = await P0305Tests.SendAsync(client,
                P0305Tests.Body(old.Quote.AmountPaise, clock.Now, first.OrderId(), payment, account: "acc_a0310"),
                "p0310_old", SecretB + "-webhook");
            Assert.Equal(HttpStatusCode.NoContent, duplicate.StatusCode);
            Assert.Equal(1L, await fixture.CountAsync("gateway_inbox_events", "configuration_version = 'a/1'"));
        }
        await P0305Tests.DrainAsync(fixture.Hosts.Licensing.Services, clock);
        Assert.Equal(1L, await fixture.CountAsync("gateway_inbox_work", "status = 'Processed'"));
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, old.Quote, 1);
        Assert.Equal(1, first.Posts);
        Assert.Equal(1, second.Posts);
        Assert.Equal(attempt, (await StartAsync(old.Payer.Client, old.Quote, HttpStatusCode.OK, key)).GetProperty("attemptId").GetGuid());
        await AssertRefundRouteAsync(fixture, old.Quote.AmountPaise, payment);
        Assert.Equal(1, first.Posts);
    }

    private static async Task AssertMissingRazorpayCredentialAsync(P0302Fixture fixture, Purchase original, Guid attempt,
        FakeRazorpay first, FakeRazorpay second)
    {
        var configuration = fixture.Hosts.Licensing.Services.GetRequiredService<IConfiguration>();
        configuration["FixtureCredentials:0:KeySecret"] = null;
        var calls = (first.Calls.Count, second.Calls.Count);
        AssertStatus(await VerifyAsync(original.Payer.Client, original.Quote, attempt), "PendingVerification", "NeedsReview", "NotApplicable");
        Assert.Equal(calls, (first.Calls.Count, second.Calls.Count));
        configuration["FixtureCredentials:0:KeySecret"] = SecretA + "-api";
    }

    private static async Task AssertRefundRouteAsync(P0302Fixture fixture, long amount, string payment)
    {
        var persisted = JsonSerializer.Deserialize<PaymentProviderPin>((string)(await LicensingPostgreSqlFixture.ReadScalarAsync(
            fixture.Hosts.LicensingOwner, """
                SELECT json_build_object('ProviderId', provider_id, 'ProviderAccountId', provider_account_id,
                    'Environment', environment, 'ConfigurationVersion', configuration_version)::text FROM public.verified_payments
                """))!)!;
        Assert.Equal(new("razorpay", "acc_a0310", "Test", "a/1"), persisted);
        using var scope = fixture.Hosts.Licensing.Services.CreateScope();
        var gateway = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>().ForPin(persisted)!;
        var probe = new RefundContractProbe(gateway);
        Assert.Equal(GatewayOutcome.Unsupported, (await probe.RequestAsync(new(persisted, payment, amount, "INR", Guid.NewGuid()))).Outcome);
        Assert.Equal(GatewayOutcome.Unsupported, (await probe.LookupAsync("contract-only")).Outcome);
        Assert.Equal(new[] { persisted, persisted }, probe.Pins);
    }

    [Fact]
    public async Task SwitchWhileOriginalCreateIsInFlightKeepsItsCommittedIdentity()
    {
        var clock = new P0201Clock { Now = WholeSecondStart };
        const string keyA = "rzp_test_a0310";
        var first = new FakeRazorpay(keyA, SecretA + "-api") { Clock = () => clock.Now, HoldWhen = c => c.StartsWith("POST ", StringComparison.Ordinal) };
        var second = new FakeRazorpay("rzp_test_b0310", SecretB, sequenceStart: 1000) { Clock = () => clock.Now };
        using var transport = new IdentityTransport(first, second, keyA, SecretA + "-api");
        await using var fixture = await P0302Fixture.StartAsync(database, clock,
            RazorpayProfiles("acc_b0310", "Test", "b/1", keyA, "rzp_test_b0310"), services =>
                services.AddHttpClient("razorpay-gateway").ConfigurePrimaryHttpMessageHandler(() => transport));
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        await SelectProfileAsync(owner, "a", 0);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        var creating = StartAsync(purchase.Payer.Client, purchase.Quote, HttpStatusCode.Created);
        try
        {
            await first.HoldReached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner,
                $"SET lock_timeout = '100ms'; SELECT pg_advisory_xact_lock(hashtextextended('b{purchase.Account:D}', 0))");
            await SelectProfileAsync(owner, "b", 1);
            Assert.Equal(1L, await fixture.CountAsync("payment_attempts", "provider_account_id = 'acc_a0310' AND selection_revision = 1"));
        }
        finally { first.ReleaseHeld.TrySetResult(); }
        await creating;
        var next = await fixture.AcceptedQuoteAsync(1);
        await StartAsync(next.Payer.Client, next.Quote, HttpStatusCode.Created);
        Assert.Equal(1L, await fixture.CountAsync("payment_attempts", "provider_account_id = 'acc_b0310' AND selection_revision = 2"));
        Assert.Equal((1, 1), (first.Posts, second.Posts));
    }

    private static Dictionary<string, string?> RazorpayProfiles(string accountB, string environmentB, string versionB, string keyA, string keyB)
    {
        var settings = Settings();
        foreach (var (index, account, environment, version, key, secret) in new[]
        { (0, "acc_a0310", "Test", "a/1", keyA, SecretA), (1, accountB, environmentB, versionB, keyB, SecretB) })
        {
            var reference = "FixtureCredentials:" + index;
            settings[$"Payments:Profiles:{index}:ProviderId"] = "razorpay";
            settings[$"Payments:Profiles:{index}:AccountId"] = account;
            settings[$"Payments:Profiles:{index}:Environment"] = environment;
            settings[$"Payments:Profiles:{index}:ConfigurationVersion"] = version;
            settings[$"Payments:Profiles:{index}:CredentialReference"] = reference;
            foreach (var (name, value) in FakeRazorpay.Settings().Where(p => p.Key.StartsWith("Payments:Razorpay:", StringComparison.Ordinal)))
                settings[reference + ":" + name[18..]] = value;
            settings[reference + ":KeyId"] = key;
            settings[reference + ":KeySecret"] = secret;
            settings[reference + ":AccountId"] = account;
            settings[reference + ":Environment"] = environment;
            settings[reference + ":ConfigurationVersion"] = version;
            settings[reference + ":WebhookSecret"] = secret + "-webhook";
            settings[reference + ":WebhookAccountId"] = account;
        }
        // Separate API and webhook credentials, as required by the real adapter.
        settings["FixtureCredentials:0:WebhookSecret"] = SecretA;
        settings["FixtureCredentials:0:KeySecret"] = SecretA + "-api";
        return settings;
    }

    private static async Task SelectProfileAsync(P0110CookieBrowser owner, string profile, int revision)
    {
        using var result = await P0212Fixture.PostAsync(owner, ProvidersPath + "/selection",
            new { profileId = profile, operationKey = Guid.NewGuid(), expectedRevision = revision, reason = "Contract fixture switch" });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    private sealed class IdentityTransport(FakeRazorpay first, FakeRazorpay second, string key, string secret) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker a = new(first, false);
        private readonly HttpMessageInvoker b = new(second, false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            (request.Headers.Authorization?.Parameter == Convert.ToBase64String(Encoding.UTF8.GetBytes(key + ":" + secret)) ? a : b).SendAsync(request, ct);
        protected override void Dispose(bool disposing)
        {
            if (disposing) { a.Dispose(); b.Dispose(); }
            base.Dispose(disposing);
        }
    }

    private sealed class RefundContractProbe(IPaymentGateway original)
    {
        internal List<PaymentProviderPin> Pins { get; } = [];
        internal Task<GatewayResult<RefundObservation>> RequestAsync(RefundRequest request)
        {
            Pins.Add(original.Pin);
            return original.RequestRefundAsync(request, default);
        }
        internal Task<GatewayResult<RefundObservation>> LookupAsync(string reference)
        {
            Pins.Add(original.Pin);
            return original.GetRefundAsync(reference, default);
        }
    }
}
