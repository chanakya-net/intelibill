using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0609Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0610Tests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task LivePinNeverDispatchesRefundPostOnLocalHostsWhileTestAndSimulatorDispatch(string hostEnvironment)
    {
        const string liveKey = "rzp_live_p0610";
        const string liveSecret = "live-fixture-secret";
        const string testKey = "rzp_test_p0610";
        const string testSecret = "test-fixture-secret";
        var liveFake = new FakeRazorpay(liveKey, liveSecret, sequenceStart: 100);
        var testFake = new FakeRazorpay(testKey, testSecret, sequenceStart: 500);
        using var transport = new PinCountingTransport(liveFake, testFake, liveKey, liveSecret);
        var settings = MultiEnvironmentSettings(liveKey, liveSecret, testKey, testSecret);

        await using var fixture = await P0609Tests.StartAsync(database, Instant, settings, services =>
            services.AddHttpClient("razorpay-gateway").ConfigurePrimaryHttpMessageHandler(() => transport));
        liveFake.Clock = () => fixture.Checkout.CheckoutClock.Now;
        testFake.Clock = () => fixture.Checkout.CheckoutClock.Now;

        using var owner = await fixture.Checkout.Trial.Catalogue.SignInOwnerAsync();
        await P0310Tests.SelectProfileAsync(owner, "live", 0);
        var (livePkg, liveReq, livePayment) = await BuyAndRequestRazorpayAsync(fixture, liveFake, "Live Fixture Shop");
        await P0310Tests.SelectProfileAsync(owner, "test", 1);
        var (testPkg, testReq, _) = await BuyAndRequestRazorpayAsync(fixture, testFake, "Test Fixture Shop");
        await P0310Tests.SelectProfileAsync(owner, "sim", 2);
        var (simPkg, simReq, _, _) = await RequestedAsync(fixture, 6000);

        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var liveReceipt = await ApproveAndReadReceiptAsync(finance, livePkg.Account, liveReq, 4500);
        await ApproveAndReadReceiptAsync(finance, testPkg.Account, testReq, 4500);
        await ApproveAndReadReceiptAsync(finance, simPkg.Account, simReq, 4500);

        await using var bgHost = fixture.Hosts.Licensing.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(hostEnvironment);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payments:ReconcileInBackground"] = "true",
            }));
            builder.ConfigureServices(services =>
                services.AddHttpClient("razorpay-gateway").ConfigurePrimaryHttpMessageHandler(() => transport));
        });
        using var _ = bgHost.CreateClient();
        Assert.Equal(hostEnvironment, bgHost.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        const string liveScope = "reservation_id IN (SELECT reservation_id FROM refund_attempts WHERE environment='Live')";
        while (await fixture.CountAsync("refund_work", "state='Confirmed' AND NOT (" + liveScope + ")") < 2
            || await fixture.CountAsync("refund_observations", liveScope) < 1)
            await Task.Delay(100, deadline.Token);

        Assert.Equal(0, RefundPosts(liveFake));
        Assert.DoesNotContain(liveFake.Calls, c => c.Contains("/refund", StringComparison.Ordinal));
        Assert.Equal(0L, await fixture.CountAsync("refund_sends", liveScope));
        Assert.Equal(1L, await fixture.CountAsync("refund_work",
            liveScope + " AND state='Reserved' AND reason='LiveDispatchDisabled' AND due_at='infinity'"));
        Assert.Equal(1, RefundPosts(testFake));
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_refunds"));
        Assert.Equal(2L, await fixture.CountAsync("refund_sends"));

        await VerifyLiveGetOnlyRecoveryAsync(fixture, liveFake, livePayment, liveReceipt, deadline.Token);
    }

    private static async Task VerifyLiveGetOnlyRecoveryAsync(P0308Fixture fixture, FakeRazorpay liveFake,
        string livePayment, string liveReceipt, CancellationToken ct)
    {
        liveFake.Refunds.Add(liveFake.RefundRecord(livePayment, liveReceipt, 4500));
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            INSERT INTO public.refund_sends (reservation_id, started_at)
            SELECT reservation_id, '{Instant:O}'::timestamptz FROM public.refund_attempts WHERE environment='Live';
            UPDATE public.refund_work SET state='Unknown', due_at='{Instant:O}'::timestamptz
            WHERE reservation_id IN (SELECT reservation_id FROM public.refund_attempts WHERE environment='Live');
            """);
        while (await fixture.CountAsync("refund_work", "state='Confirmed'") < 3)
            await Task.Delay(100, ct);
        Assert.Equal(0, RefundPosts(liveFake));
        Assert.Equal(3L, await fixture.CountAsync("refund_work", "state='Confirmed'"));
    }

    [Fact]
    public async Task BelowMinimumRefundIsDurablyHeldWithBoundedSchedulingWhileMinimumDispatches()
    {
        var fake = new FakeRazorpay("rzp_test_min", "min-secret");
        var settings = RazorpayPlusSimulatorSettings("rzp_test_min", "min-secret");
        await using var fixture = await P0609Tests.StartAsync(database, Instant, settings, services =>
            services.AddHttpClient("razorpay-gateway").ConfigurePrimaryHttpMessageHandler(() => fake));
        fake.Clock = () => fixture.Checkout.CheckoutClock.Now;

        using var owner = await fixture.Checkout.Trial.Catalogue.SignInOwnerAsync();
        await P0310Tests.SelectProfileAsync(owner, "rzp", 0);
        var (package, belowMin, confirmation, order) = await BuyAndRequestRazorpayWithIdsAsync(fixture, fake, 99);
        var atMin = await RequestRefundAsync(package, order, confirmation, 100);
        await P0310Tests.SelectProfileAsync(owner, "sim", 1);

        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var belowPreview = await PreviewAsync(finance, package.Account, belowMin, 99, "EndAt", Instant.AddDays(3));
        await ReadAsync(await ApproveAsync(finance, package.Account, belowMin, Command(belowPreview, 99, "EndAt", Instant.AddDays(3))),
            HttpStatusCode.Created);
        await ApproveAndReadReceiptAsync(finance, package.Account, atMin, 100);

        for (var hour = 0; hour < 15; hour++)
        {
            fixture.At(Instant.AddHours(hour));
            await PumpRefundsAsync(fixture);
            await PumpRefundsAsync(fixture);
        }

        var belowScope = $"reservation_id IN (SELECT reservation_id FROM refund_attempts WHERE refund_request_id='{belowMin:D}')";
        var minScope = $"reservation_id IN (SELECT reservation_id FROM refund_attempts WHERE refund_request_id='{atMin:D}')";
        Assert.Equal(1, RefundPosts(fake));
        Assert.Equal(0L, await fixture.CountAsync("development_gateway_refunds"));
        Assert.Equal(0L, await fixture.CountAsync("refund_sends", belowScope));
        Assert.Equal(1L, await fixture.CountAsync("refund_observations", belowScope));
        Assert.Equal(1L, await fixture.CountAsync("refund_work",
            belowScope + " AND state='Reserved' AND reason='BelowProviderMinimum' AND due_at='infinity'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_approvals", $"refund_request_id='{belowMin:D}' AND amount_paise=99"));
        Assert.Equal(1L, await fixture.CountAsync("refund_sends", minScope));
        Assert.Equal(1L, await fixture.CountAsync("refund_work", minScope + " AND state='Confirmed'"));
        Assert.Equal(100L, Assert.Single(fake.Refunds)["amount"]!.GetValue<long>());

        var belowItem = Assert.Single((await ReadAsync(await finance.SendAsync(HttpMethod.Get,
            AdminPath(package.Account) + $"?request={belowMin:D}"), HttpStatusCode.OK)).GetProperty("items").EnumerateArray());
        Assert.Equal("Pending", belowItem.GetProperty("status").GetString());
        Assert.Equal("Reserved", belowItem.GetProperty("providerState").GetString());
        Assert.Equal(99L, belowItem.GetProperty("approvedAmountPaise").GetInt64());
        Assert.Equal("BelowProviderMinimum", belowItem.GetProperty("observationReason").GetString());
        Assert.Equal("AwaitingConfirmation", belowItem.GetProperty("accessDisposition").GetString());
    }

    [Fact]
    public async Task TemporarilyMissingRazorpayCredentialsStayRecoverableAfterRestoration()
    {
        var fake = new FakeRazorpay("rzp_test_cred", "cred-secret");
        var settings = RazorpayPlusSimulatorSettings("rzp_test_cred", "cred-secret");
        await using var fixture = await P0609Tests.StartAsync(database, Instant, settings, services =>
            services.AddHttpClient("razorpay-gateway").ConfigurePrimaryHttpMessageHandler(() => fake));
        fake.Clock = () => fixture.Checkout.CheckoutClock.Now;

        using var owner = await fixture.Checkout.Trial.Catalogue.SignInOwnerAsync();
        await P0310Tests.SelectProfileAsync(owner, "rzp", 0);
        var (package, request, _, _) = await BuyAndRequestRazorpayWithIdsAsync(fixture, fake, 100);
        await P0310Tests.SelectProfileAsync(owner, "sim", 1);

        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await ApproveAndReadReceiptAsync(finance, package.Account, request, 100);

        var config = fixture.Hosts.Licensing.Services.GetRequiredService<IConfiguration>();
        config["FixtureCredentials:Rzp:KeySecret"] = null;
        await PumpRefundsAsync(fixture);
        Assert.Equal(0, RefundPosts(fake));
        Assert.Equal(0L, await fixture.CountAsync("refund_sends"));
        Assert.Equal(1L, await fixture.CountAsync("refund_work",
            "state='Reserved' AND reason='OriginalAdapterUnavailable' AND due_at <> 'infinity'"));

        config["FixtureCredentials:Rzp:KeySecret"] = "cred-secret";
        fixture.At(Instant.AddMinutes(5));
        await PumpRefundsAsync(fixture);
        Assert.Equal(1, RefundPosts(fake));
        Assert.Equal(1L, await fixture.CountAsync("refund_sends"));
        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Confirmed' AND reason='OriginalProviderRead'"));
        Assert.Equal(0L, await fixture.CountAsync("development_gateway_refunds"));
    }

    private static async Task<(Package Package, Guid Request, string Payment)> BuyAndRequestRazorpayAsync(
        P0308Fixture fixture, FakeRazorpay fake, string shopName)
    {
        var package = await fixture.PackageAsync(1, [shopName]);
        var quote = await AcceptAsync(package, await FirstQuoteAsync(package, "Month"));
        var attempt = await StartPaymentAsync(package, quote);
        var orderId = fake.OrderIds().Last();
        var payment = fake.AddPayment(orderId, "captured");
        using var verify = await package.Payer.Client.PostAsJsonAsync(P0302Fixture.VerifyPath(quote, attempt),
            new { launcherOutcome = (string?)null, launcherProof = (Dictionary<string, string>?)null });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        await P0608Tests.DrainAsync(fixture);
        var confirmation = await P0608Tests.ConfirmationIdAsync(package.Payer.Client, package.Account, quote.OrderId);
        var request = await RequestRefundAsync(package, quote.OrderId, confirmation, 6000);
        return (package, request, payment);
    }

    private static async Task<(Package Package, Guid Request, Guid Confirmation, Guid Order)> BuyAndRequestRazorpayWithIdsAsync(
        P0308Fixture fixture, FakeRazorpay fake, long paise)
    {
        var package = await fixture.PackageAsync(1, ["Minimum Shop"]);
        var quote = await AcceptAsync(package, await FirstQuoteAsync(package, "Month"));
        var attempt = await StartPaymentAsync(package, quote);
        fake.AddPayment(fake.OrderIds().Last(), "captured");
        using var verify = await package.Payer.Client.PostAsJsonAsync(P0302Fixture.VerifyPath(quote, attempt),
            new { launcherOutcome = (string?)null, launcherProof = (Dictionary<string, string>?)null });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        await P0608Tests.DrainAsync(fixture);
        var confirmation = await P0608Tests.ConfirmationIdAsync(package.Payer.Client, package.Account, quote.OrderId);
        var request = await RequestRefundAsync(package, quote.OrderId, confirmation, paise);
        return (package, request, confirmation, quote.OrderId);
    }

    private static Dictionary<string, string?> MultiEnvironmentSettings(string liveKey, string liveSecret, string testKey, string testSecret)
    {
        var settings = new Dictionary<string, string?>(P0302Fixture.SimulatorSettings);
        AddRazorpayProfile(settings, 0, "live", "acc_live919", "Live", "live/1", "FixtureCredentials:Live", liveKey, liveSecret);
        AddRazorpayProfile(settings, 1, "test", "acc_test919", "Test", "test/1", "FixtureCredentials:Test", testKey, testSecret);
        settings["Payments:Profiles:2:Id"] = "sim";
        settings["Payments:Profiles:2:ProviderId"] = "development-simulator";
        settings["Payments:Profiles:2:AccountId"] = "simulator-919";
        settings["Payments:Profiles:2:Environment"] = "Development";
        settings["Payments:Profiles:2:ConfigurationVersion"] = "sim/1";
        return settings;
    }

    private static Dictionary<string, string?> RazorpayPlusSimulatorSettings(string keyId, string secret)
    {
        var settings = new Dictionary<string, string?>(P0302Fixture.SimulatorSettings);
        AddRazorpayProfile(settings, 0, "rzp", "acc_min919", "Test", "min/1", "FixtureCredentials:Rzp", keyId, secret);
        settings["Payments:Profiles:1:Id"] = "sim";
        settings["Payments:Profiles:1:ProviderId"] = "development-simulator";
        settings["Payments:Profiles:1:AccountId"] = "simulator-min";
        settings["Payments:Profiles:1:Environment"] = "Development";
        settings["Payments:Profiles:1:ConfigurationVersion"] = "sim/1";
        return settings;
    }

    private static void AddRazorpayProfile(Dictionary<string, string?> settings, int index, string id, string account,
        string environment, string version, string reference, string keyId, string secret)
    {
        settings[$"Payments:Profiles:{index}:Id"] = id;
        settings[$"Payments:Profiles:{index}:ProviderId"] = "razorpay";
        settings[$"Payments:Profiles:{index}:AccountId"] = account;
        settings[$"Payments:Profiles:{index}:Environment"] = environment;
        settings[$"Payments:Profiles:{index}:ConfigurationVersion"] = version;
        settings[$"Payments:Profiles:{index}:CredentialReference"] = reference;
        foreach (var (name, value) in FakeRazorpay.Settings().Where(p => p.Key.StartsWith("Payments:Razorpay:", StringComparison.Ordinal)))
            settings[reference + ":" + name[18..]] = value;
        settings[reference + ":KeyId"] = keyId;
        settings[reference + ":KeySecret"] = secret;
        settings[reference + ":AccountId"] = account;
        settings[reference + ":Environment"] = environment;
        settings[reference + ":ConfigurationVersion"] = version;
    }

    private sealed class PinCountingTransport(FakeRazorpay live, FakeRazorpay test, string liveKey, string liveSecret) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker liveInvoker = new(live, false);
        private readonly HttpMessageInvoker testInvoker = new(test, false);
        private readonly string liveAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes(liveKey + ":" + liveSecret));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            (request.Headers.Authorization?.Parameter == liveAuth ? liveInvoker : testInvoker).SendAsync(request, ct);
        protected override void Dispose(bool disposing)
        {
            if (disposing) { liveInvoker.Dispose(); testInvoker.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
