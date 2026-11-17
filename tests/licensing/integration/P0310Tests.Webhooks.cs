using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Intelibill.Licensing.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0310Tests
{
    private const string SecretA = "p0310-simulator-a-webhook-fixture";
    private const string SecretB = "p0310-simulator-b-webhook-fixture";

    internal static Dictionary<string, string?> WebhookSettings(bool ambiguous = false)
    {
        var settings = Settings();
        settings["Payments:Profiles:0:WebhookSecretReference"] = "FixtureSecrets:A";
        settings["Payments:Profiles:1:WebhookSecretReference"] = "FixtureSecrets:B";
        settings["FixtureSecrets:A"] = SecretA;
        settings["FixtureSecrets:B"] = ambiguous ? SecretA : SecretB;
        return settings;
    }

    [Fact]
    public async Task AmbiguousAuthenticatedWebhookNeverChoosesFirstAccount()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart }, WebhookSettings(true));
        using var client = fixture.Hosts.Licensing.CreateClient();
        using var response = await SimulatorEventAsync(client, "dev_order_fixture", "ambiguous", SecretA);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0L, await fixture.CountAsync("gateway_inbox_events"));
    }

    [Fact]
    public async Task ProviderSwitchContractPreservesWebhookAndRefundPins()
    {
        var clock = new P0201Clock { Now = WholeSecondStart };
        await using var fixture = await P0302Fixture.StartAsync(database, clock, WebhookSettings());
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        using (var selected = await P0212Fixture.PostAsync(owner, ProvidersPath + "/selection",
            new { profileId = "a", operationKey = Guid.NewGuid(), expectedRevision = 0, reason = "Select original A" }))
            Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        var attempt = (await StartAsync(purchase.Payer.Client, purchase.Quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
        using (var switched = await P0212Fixture.PostAsync(owner, ProvidersPath + "/selection",
            new { profileId = "b", operationKey = Guid.NewGuid(), expectedRevision = 1, reason = "Select future B" }))
            Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        await CaptureAsync(purchase.Payer.Client, purchase.Quote, attempt);
        var reference = (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.LicensingOwner,
            "SELECT provider_order_reference FROM public.payment_attempt_sessions"))!;
        using var client = fixture.Hosts.Licensing.CreateClient();
        for (var i = 0; i < 2; i++)
        {
            using var response = await SimulatorEventAsync(client, reference, "old-capture", SecretA);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        await P0305Tests.DrainAsync(fixture.Hosts.Licensing.Services, clock);
        Assert.Equal(1L, await fixture.CountAsync("gateway_inbox_events"));
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, purchase.Quote, 1);
        Assert.Equal(1L, await fixture.CountAsync("verified_payments", "provider_account_id = 'simulator-a' AND configuration_version = 'a/1'"));
        using var scope = fixture.Hosts.Licensing.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>();
        var original = Assert.IsAssignableFrom<IPaymentGateway>(resolver.ForPin(A));
        Assert.Equal(A, original.Pin);
        Assert.Equal(B, (await scope.ServiceProvider.GetRequiredService<IPaymentProviderSelection>().SelectedAsync(false, default)).Pin);
        Assert.False(original.Capabilities.Refunds);
        Assert.Equal(GatewayOutcome.Unsupported, (await original.RequestRefundAsync(new(A, "unused-contract-only", 12000, "INR", Guid.NewGuid()), default)).Outcome);
        Assert.Equal(GatewayOutcome.Unsupported, (await original.GetRefundAsync("unused-contract-only", default)).Outcome);
    }

    internal static async Task<HttpResponseMessage> SimulatorEventAsync(HttpClient client, string order, string id, string secret)
    {
        var body = JsonSerializer.Serialize(new { kind = "PaymentCaptured", orderReference = order });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhooks/development-simulator");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.Add("X-Intelibill-Event-Id", id);
        request.Headers.Add("X-Intelibill-Webhook-Signature", Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))));
        return await client.SendAsync(request);
    }
}
