using System.Net;
using System.Text;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0305Tests
{
    [Fact]
    public async Task SignedSimulatorCaptureRetriesUntilTheOriginalPaymentIsCaptured()
    {
        var clock = new P0201Clock { Now = WholeSecondStart };
        var settings = new Dictionary<string, string?>(SimulatorSettings) { ["Payments:Simulator:WebhookSecret"] = Secret };
        await using var fixture = await P0302Fixture.StartAsync(database, clock, settings);
        var (payer, _, _, quote) = await fixture.AcceptedQuoteAsync(1);
        var started = await P0302Fixture.StartAsync(payer.Client, quote, HttpStatusCode.Created);
        var attempt = started.GetProperty("attemptId").GetGuid();
        var order = (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.LicensingOwner,
            "SELECT provider_order_reference FROM development_gateway_sessions"))!;
        using var client = fixture.Hosts.Licensing.CreateClient();
        var services = fixture.Hosts.Licensing.Services;
        await PostAsync(payer.Client, CapturePath(quote, attempt), new { outcome = "Delayed" });

        Assert.Equal(HttpStatusCode.Unauthorized, await SimulatorDeliveryAsync(client, order, "capture", "PaymentCaptured", "wrong"));
        Assert.Equal(HttpStatusCode.NoContent, await SimulatorDeliveryAsync(client, order, "capture", "PaymentCaptured", Secret));
        Assert.Equal(HttpStatusCode.NoContent, await SimulatorDeliveryAsync(client, order, "capture", "PaymentCaptured", Secret));
        await DrainAsync(services, clock);
        await AssertWorkAsync(fixture, "Pending", "CaptureUnproven", failure: true);
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        clock.Now = clock.Now.AddSeconds(30);
        await DrainAsync(services, clock);
        await AssertWorkAsync(fixture, "Processed", "Fulfilled");

        Assert.Equal(HttpStatusCode.NoContent, await SimulatorDeliveryAsync(client, order, "failure", "PaymentFailed", Secret));
        await DrainAsync(services, clock);
        Assert.Equal(2L, await fixture.CountAsync("gateway_inbox_events"));
        AssertStatus(await StatusAsync(payer.Client, quote), "Verified", "Fulfilled", "Applying");
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, quote, 1);
        await RecordAsync("webhook-simulator.json", new { delayedSeconds = 30, deliveries = 4, rows = 2, fulfilments = 1 });
    }

    private static async Task<HttpStatusCode> SimulatorDeliveryAsync(HttpClient client, string order, string id, string kind, string secret)
    {
        var body = JsonSerializer.Serialize(new { kind, orderReference = order });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhooks/development-simulator")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Intelibill-Event-Id", id);
        request.Headers.Add("X-Intelibill-Webhook-Signature", Sign(body, secret));
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }
}
