using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0310Tests
{
    [Theory]
    [InlineData("ProviderId", "not-loaded", "MissingProfile")]
    [InlineData("AccountId", "missing-a", "MissingProfile")]
    [InlineData("Environment", "Test", "MissingProfile")]
    [InlineData("ConfigurationVersion", "a/not-loaded", "MissingProfile")]
    [InlineData("WebhookSecretReference", "Missing:Secret", "CredentialUnavailable")]
    public async Task MissingHistoricalConfigurationNeedsReviewAndRecoversOriginalAttempt(string field, string missing, string reason)
    {
        var clock = new P0201Clock { Now = WholeSecondStart };
        await using var fixture = await P0302Fixture.StartAsync(database, clock, WebhookSettings());
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        using (var selected = await P0212Fixture.PostAsync(owner, ProvidersPath + "/selection",
            new { profileId = "a", operationKey = Guid.NewGuid(), expectedRevision = 0, reason = "Initial profile A" }))
            Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        var key = Guid.NewGuid();
        var started = await StartAsync(purchase.Payer.Client, purchase.Quote, HttpStatusCode.Created, key);
        var attempt = started.GetProperty("attemptId").GetGuid();
        await PostAsync(purchase.Payer.Client, CapturePath(purchase.Quote, attempt), new { outcome = "Delayed" });
        using (var switched = await P0212Fixture.PostAsync(owner, ProvidersPath + "/selection",
            new { profileId = "b", operationKey = Guid.NewGuid(), expectedRevision = 1, reason = "Use B for future orders" }))
            Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        await using var unavailable = fixture.Hosts.Licensing.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { [$"Payments:Profiles:0:{field}"] = missing })));
        await using var merchant = fixture.Hosts.Merchant.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient("MerchantBillingDelegation").ConfigurePrimaryHttpMessageHandler(() => unavailable.Server.CreateHandler())));
        using var reopened = merchant.CreateClient();
        reopened.DefaultRequestHeaders.Authorization = purchase.Payer.Client.DefaultRequestHeaders.Authorization;
        AssertStatus(await StatusAsync(reopened, purchase.Quote), "PendingVerification", "NeedsReview", "NotApplicable");
        for (var index = 0; index < 2; index++)
        {
            var review = await VerifyAsync(reopened, purchase.Quote, attempt);
            AssertStatus(review, "PendingVerification", "NeedsReview", "NotApplicable");
            Assert.Equal(attempt, review.GetProperty("attemptId").GetGuid());
            Assert.False(review.GetProperty("canStart").GetBoolean());
            Assert.Equal(JsonValueKind.Null, review.GetProperty("launch").ValueKind);
        }
        Assert.Equal(1L, await fixture.CountAsync("payment_routing_events", $"kind = 'Unavailable' AND reason = '{reason}'"));
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_sessions"));
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        clock.Now = clock.Now.AddSeconds(31);
        AssertStatus(await VerifyAsync(purchase.Payer.Client, purchase.Quote, attempt), "Verified", "Fulfilled", "Applying");
        Assert.Equal(1L, await fixture.CountAsync("payment_routing_events", "kind = 'Restored'"));
        AssertStatus(await StatusAsync(reopened, purchase.Quote), "Verified", "Fulfilled", "Applying");
        await fixture.Delivery.DrainAsync(source: true);
        await fixture.Delivery.DrainAsync(source: false);
        AssertStatus(await StatusAsync(reopened, purchase.Quote), "Verified", "Fulfilled", "Applied");
        Assert.Equal(attempt, (await StartAsync(purchase.Payer.Client, purchase.Quote, HttpStatusCode.OK, key)).GetProperty("attemptId").GetGuid());
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, purchase.Quote, 1);
    }
}
