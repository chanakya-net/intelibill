using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0310Tests
{
    [Fact]
    public async Task BackgroundAndInboxRecoveryKeepMissingOriginalConfigurationVisible()
    {
        var clock = new P0201Clock { Now = WholeSecondStart };
        var settings = WebhookSettings();
        settings["Payments:Mode"] = "Disabled";
        settings["Payments:ReconcileInBackground"] = "false";
        await using var fixture = await P0302Fixture.StartAsync(database, clock, settings);
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        await SelectProfileAsync(owner, "a", 0);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        var attempt = (await StartAsync(purchase.Payer.Client, purchase.Quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
        await PostAsync(purchase.Payer.Client, CapturePath(purchase.Quote, attempt), new { outcome = "Delayed" });
        await SelectProfileAsync(owner, "b", 1);
        var reference = (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.LicensingOwner,
            "SELECT provider_order_reference FROM public.payment_attempt_sessions"))!;
        using var client = fixture.Hosts.Licensing.CreateClient();
        using (var received = await SimulatorEventAsync(client, reference, "missing-original-worker", SecretA))
            Assert.Equal(HttpStatusCode.NoContent, received.StatusCode);
        // Start recovery with A already absent: no pre-removal adapter read may race the fixed-clock advance.
        await using var worker = fixture.Hosts.Licensing.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            { ["Payments:ReconcileInBackground"] = "true", ["Payments:Profiles:0:AccountId"] = "not-loaded-a" })));
        using var running = worker.CreateClient();
        var configuration = worker.Services.GetRequiredService<IConfiguration>();
        clock.Now = clock.Now.AddSeconds(31);
        await P0305Tests.DrainAsync(worker.Services, clock);
        await AwaitWorkerEvidenceAsync(fixture, "gateway_inbox_work", "last_failure = 'AdapterUnavailable'");
        await AwaitWorkerEvidenceAsync(fixture, "pending_payment_attempts", "attempts > 0 AND NOT completed");
        await AwaitWorkerEvidenceAsync(fixture, "payment_routing_events", "kind = 'Unavailable'");
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_sessions", "provider_account_id = 'simulator-a'"));
        Assert.Equal(0L, await fixture.CountAsync("development_gateway_sessions", "provider_account_id = 'simulator-b'"));
        configuration["Payments:Profiles:0:AccountId"] = A.ProviderAccountId;
        clock.Now = clock.Now.AddMinutes(1);
        await P0305Tests.DrainAsync(worker.Services, clock);
        await AwaitWorkerEvidenceAsync(fixture, "verified_payments", "provider_account_id = 'simulator-a'");
        AssertStatus(await StatusAsync(purchase.Payer.Client, purchase.Quote), "Verified", "Fulfilled", "Applying");
        Assert.Equal(1L, await fixture.CountAsync("payment_routing_events", "kind = 'Restored'"));
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, purchase.Quote, 1);
    }

    private static async Task AwaitWorkerEvidenceAsync(P0302Fixture fixture, string table, string where)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline && await fixture.CountAsync(table, where) == 0) await Task.Delay(100);
        Assert.Equal(1L, await fixture.CountAsync(table, where));
    }
}
