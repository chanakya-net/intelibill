using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0312Tests
{
    [Fact]
    public async Task UnknownOriginalAttemptAfterSwitchSettlesOnceWithOriginalConfirmation()
    {
        var clock = new P0201Clock { Now = WholeSecondStart };
        var probe = new P0302Tests.GatewayProbe { LoseCreateResponse = true, LookupNotFound = true };
        var settings = P0310Tests.WebhookSettings();
        await using var fixture = await P0302Fixture.StartAsync(database, clock, settings, probe.Install);
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        await SelectAsync(owner, "a", 0);
        var purchase = await fixture.AcceptedQuoteAsync(3, ["Original A1", "Original A2", "Original A3"]);
        probe.Database = fixture.Hosts.LicensingOwner;
        probe.Account = purchase.Account;
        var key = Guid.NewGuid();
        var attempt = (await StartAsync(purchase.Payer.Client, purchase.Quote, HttpStatusCode.Created, key)).GetProperty("attemptId").GetGuid();
        var reference = Assert.IsType<string>(probe.OriginalReference);
        Assert.Single(probe.Observations, call => call.StartsWith("create:", StringComparison.Ordinal));
        await RecoverUnknownAsync(fixture, purchase, probe, attempt);
        await AssertUnknownAsync(fixture, purchase, attempt, key);
        await SelectAsync(owner, "b", 1);
        var next = await fixture.AcceptedQuoteAsync(1);
        probe.Account = next.Account;
        probe.LoseCreateResponse = false;
        await StartAsync(next.Payer.Client, next.Quote, HttpStatusCode.Created);
        probe.Account = purchase.Account;
        await RecoverUnknownAsync(fixture, purchase, probe, attempt);
        Assert.Equal(4, probe.LookupCalls);
        Assert.All(probe.LookupPins, pin => Assert.Equal(P0310Tests.A, pin));
        await RefuseReplacementAsync(fixture, purchase, attempt);
        await AssertUnknownAsync(fixture, purchase, attempt, key);
        var createCalls = probe.Observations.Count(call => call.StartsWith("create:", StringComparison.Ordinal));
        Assert.Equal(2, createCalls);
        await ObserveAsync(fixture, purchase, "switch-unknown", new { key, attempt, reference, probe.LookupCalls, probe.LookupPins, createCalls });
        await SettleOriginalAsync(fixture, purchase, probe, attempt, reference, settings["FixtureSecrets:A"]!);
        await AssertPackageAsync(fixture, purchase);
        var receipts = await ConfirmationsAsync(purchase);
        var facts = await FactsAsync(fixture, Assert.Single(receipts).Receipt.ConfirmationId);
        Assert.Equal((P0310Tests.A.ProviderId, P0310Tests.A.ProviderAccountId, P0310Tests.A.Environment, P0310Tests.A.ConfigurationVersion),
            (facts.ProviderId, facts.ProviderAccountId, facts.Environment, facts.ConfigurationVersion));
        Assert.Equal(reference, facts.ProviderOrderReference);
        await ObserveAsync(fixture, purchase, "switch-captured-applying");
        await fixture.Delivery.DrainAsync(source: true);
        await fixture.Delivery.DrainAsync(source: false);
        await AssertDeliveryAsync(fixture, purchase, 3);
        await VerifyAsync(purchase.Payer.Client, purchase.Quote, attempt);
        await AssertSameConfirmationsAsync(purchase, receipts);
        Assert.Equal(2, probe.Observations.Count(call => call.StartsWith("create:", StringComparison.Ordinal)));
        await ObserveAsync(fixture, purchase, "switch-applied", new { createCalls });
    }

    private static async Task RecoverUnknownAsync(P0302Fixture fixture, Purchase purchase, P0302Tests.GatewayProbe probe, Guid attempt)
    {
        var lookups = probe.LookupCalls;
        await VerifyAsync(purchase.Payer.Client, purchase.Quote, attempt);
        Assert.Equal(lookups + 1, probe.LookupCalls);
        fixture.CheckoutClock.Now += TimeSpan.FromSeconds(30);
        var lease = Assert.IsType<PendingPaymentLease>(await P0303Tests.ClaimAsync(fixture.Hosts.Licensing.Services, fixture.CheckoutClock));
        Assert.Equal(attempt, lease.AttemptId);
        Assert.False(await P0303Tests.ApplyLeaseAsync(fixture.Hosts.Licensing.Services, lease));
        Assert.Equal(lookups + 2, probe.LookupCalls);
    }

    private static async Task RefuseReplacementAsync(P0302Fixture fixture, Purchase purchase, Guid attempt)
    {
        using var replacement = await purchase.Payer.Client.PostAsJsonAsync(AttemptsPath(purchase.Quote), Start(purchase.Quote));
        Assert.Equal(HttpStatusCode.Conflict, replacement.StatusCode);
        Assert.Equal(CheckoutPaymentsV1.AlreadyStarted, await P0206Fixture.CodeAsync(replacement));
        var original = $"attempt_id IN (SELECT attempt_id FROM payment_attempts WHERE order_id = '{purchase.Quote.OrderId:D}')";
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_sessions", original));
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_sessions", $"{original} AND attempt_id = '{attempt:D}' AND provider_account_id = 'simulator-a'"));
    }

    private static async Task SettleOriginalAsync(P0302Fixture fixture, Purchase purchase, P0302Tests.GatewayProbe probe,
        Guid attempt, string reference, string secret)
    {
        fixture.CheckoutClock.Now = purchase.Quote.ExpiresAt.AddMinutes(2);
        probe.LookupNotFound = false;
        await using (var scope = fixture.Hosts.Licensing.Services.CreateAsyncScope())
        {
            var gateway = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>().ForPin(P0310Tests.A);
            await Assert.IsAssignableFrom<IPaymentSimulator>(gateway).SimulateAsync(reference, "Captured", default);
        }
        using var ingress = fixture.Hosts.Licensing.CreateClient();
        for (var index = 0; index < 2; index++)
        {
            using var received = await P0310Tests.SimulatorEventAsync(ingress, reference, "p0312-original-a", secret);
            Assert.Equal(HttpStatusCode.NoContent, received.StatusCode);
        }
        await Task.WhenAll(VerifyAsync(purchase.Payer.Client, purchase.Quote, attempt),
            P0305Tests.DrainAsync(fixture.Hosts.Licensing.Services, fixture.CheckoutClock));
        fixture.CheckoutClock.Now += TimeSpan.FromSeconds(4);
        await P0305Tests.DrainAsync(fixture.Hosts.Licensing.Services, fixture.CheckoutClock);
        Assert.Equal(1L, await fixture.CountAsync("gateway_inbox_events"));
        Assert.Equal(1L, await fixture.CountAsync("gateway_inbox_work", "status = 'Processed'"));
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_sessions", "provider_account_id = 'simulator-a'"));
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_sessions", "provider_account_id = 'simulator-b'"));
        AssertStatus(await StatusAsync(purchase.Payer.Client, purchase.Quote), "Verified", "Fulfilled", "Applying");
    }

    private static async Task AssertUnknownAsync(P0302Fixture fixture, Purchase purchase, Guid attempt, Guid key)
    {
        var status = await StatusAsync(purchase.Payer.Client, purchase.Quote);
        AssertStatus(status, "PendingVerification", "NotFulfilled", "NotApplicable");
        Assert.False(status.GetProperty("canStart").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("launch").ValueKind);
        Assert.Equal(attempt, (await StartAsync(purchase.Payer.Client, purchase.Quote, HttpStatusCode.OK, key)).GetProperty("attemptId").GetGuid());
        Assert.Empty(await ConfirmationsAsync(purchase));
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await fixture.CountAsync("payment_fulfilments"));
        Assert.Equal(0L, await fixture.CountAsync("subscription_term_facts", "basis = 'Paid'"));
        Assert.Equal(1L, await fixture.CountAsync("payment_attempts", "provider_account_id = 'simulator-a' AND environment = 'Development' AND configuration_version = 'a/1'"));
        Assert.Equal(0L, await fixture.CountAsync("payment_attempt_sessions", $"attempt_id = '{attempt:D}'"));
    }

    private static async Task SelectAsync(P0110CookieBrowser owner, string profile, int revision)
    {
        using var selected = await P0212Fixture.PostAsync(owner, "/api/admin/payment-providers/selection",
            new { operationKey = Guid.NewGuid(), expectedRevision = revision, profileId = profile, reason = "P03-12 local retained-provider acceptance" });
        Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
    }
}
