using System.Net;
using System.Net.Http.Json;
using Intelibill.Licensing.Application.Payments;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0312Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ManualOnlineRaceRetainsTwoReceiptsOnePackageAndOneAcknowledgedTerm(bool manualWins)
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart }, P0306Tests.Settings);
        var purchase = await fixture.AcceptedQuoteAsync(3, ["East", "West", "Centre"]);
        var key = Guid.NewGuid();
        var attempt = (await StartAsync(purchase.Payer.Client, purchase.Quote, HttpStatusCode.Created, key)).GetProperty("attemptId").GetGuid();
        await CaptureAsync(purchase.Payer.Client, purchase.Quote, attempt);
        using var billing = await P0306Tests.SignInBillingAsync(fixture);
        var command = P0306Tests.Receipt(purchase.Quote, WholeSecondStart);
        var chain = await RaceAsync(fixture, purchase, billing, command, attempt, manualWins);
        await AssertPackageAsync(fixture, purchase);
        var receipts = await ConfirmationsAsync(purchase);
        Assert.Equal(2, receipts.Length);
        Assert.Single(receipts, r => r.Receipt.Source == "Online");
        Assert.Single(receipts, r => r.Receipt.Source == "Manual");
        var facts = await Task.WhenAll(receipts.Select(r => FactsAsync(fixture, r.Receipt.ConfirmationId)));
        Assert.Single(facts, f => f.FulfilmentAtIssuance == "NeedsReview");
        Assert.All(facts, f => Assert.Equal((purchase.Quote.AmountPaise, "INR", 3), (f.Receipt.AmountPaise, f.Receipt.Currency, f.Terms.Shops.Length)));
        var manualFacts = Assert.Single(facts, f => f.Receipt.Source == "Manual");
        Assert.Equal(("manual", "fixture-bank", "Test", "fixture/1"),
            (manualFacts.ProviderId, manualFacts.ProviderAccountId, manualFacts.Environment, manualFacts.ConfigurationVersion));
        var onlineFacts = Assert.Single(facts, f => f.Receipt.Source == "Online");
        Assert.Equal(("development-simulator", "local-simulator", "Development", "development-simulator/1"),
            (onlineFacts.ProviderId, onlineFacts.ProviderAccountId, onlineFacts.Environment, onlineFacts.ConfigurationVersion));
        Assert.Equal(attempt, onlineFacts.ReceiptReference);
        Assert.False(string.IsNullOrEmpty(onlineFacts.ProviderPaymentReference));
        Assert.Equal(1L, await fixture.CountAsync("manual_payment_receipts", $"order_id = '{purchase.Quote.OrderId:D}'"));
        Assert.Equal(manualWins ? 0L : 1L, await fixture.CountAsync("manual_payment_receipts", "review_reason = 'DuplicateFunds'"));
        Assert.Equal(manualWins ? 1L : 0L, await fixture.CountAsync("payment_events", "kind = 'PaidNeedsReview'"));
        Assert.Equal(1L, await fixture.CountAsync("unmatched_payments", $"order_id = '{purchase.Quote.OrderId:D}' AND classification = 'DuplicateFunds'"));
        await AssertRaceReplaysAsync(fixture, purchase, billing, command, attempt, key);
        await ObserveAsync(fixture, purchase, $"race-manual-{manualWins}-applying", new { manualWins, chain, facts });
        await fixture.Delivery.DrainAsync(source: true);
        await fixture.Delivery.DrainAsync(source: false);
        await AssertDeliveryAsync(fixture, purchase, 3);
        await AssertSameConfirmationsAsync(purchase, receipts);
        await ObserveAsync(fixture, purchase, $"race-manual-{manualWins}-applied");
    }

    private static async Task<object> RaceAsync(P0302Fixture fixture, Purchase purchase, P0110CookieBrowser billing,
        ConfirmManualPayment command, Guid attempt, bool manualWins)
    {
        await using var fence = await P0302Tests.FenceAsync(fixture, "payment_fulfilments");
        Task<HttpResponseMessage> Manual() => P0212Fixture.PostAsync(billing, P0306Tests.Path(purchase.Quote), command);
        Task<HttpResponseMessage> Online() => purchase.Payer.Client.PostAsJsonAsync(VerifyPath(purchase.Quote, attempt), new { launcherOutcome = (string?)null });
        var first = manualWins ? Manual() : Online();
        var firstPid = Assert.Single(await P0302Tests.WaitForBlockedChainAsync(fixture, fence.Pid, 1));
        var second = manualWins ? Online() : Manual();
        var chain = await P0302Tests.WaitForBlockedChainAsync(fixture, fence.Pid, 2);
        Assert.Contains(firstPid, chain);
        await fence.ReleaseAsync();
        using var manual = await (manualWins ? first : second);
        using var online = await (manualWins ? second : first);
        Assert.Equal(HttpStatusCode.Created, manual.StatusCode);
        var body = await P0206Fixture.BodyAsync(manual);
        Assert.Equal(manualWins ? "Fulfilled" : "NeedsReview", body.GetProperty("fulfilment").GetString());
        if (!manualWins) Assert.Equal("DuplicateFunds", body.GetProperty("reviewReason").GetString());
        Assert.Equal(HttpStatusCode.OK, online.StatusCode);
        return new { fencePid = fence.Pid, firstPid, blockedPids = chain };
    }

    private static async Task AssertRaceReplaysAsync(P0302Fixture fixture, Purchase purchase, P0110CookieBrowser billing,
        ConfirmManualPayment command, Guid attempt, Guid key)
    {
        using var replay = await P0212Fixture.PostAsync(billing, P0306Tests.Path(purchase.Quote), command);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        await VerifyAsync(purchase.Payer.Client, purchase.Quote, attempt);
        Assert.Equal(attempt, (await StartAsync(purchase.Payer.Client, purchase.Quote, HttpStatusCode.OK, key)).GetProperty("attemptId").GetGuid());
        using var changed = await P0212Fixture.PostAsync(billing, P0306Tests.Path(purchase.Quote), command with { Reason = "Changed payload" });
        Assert.Equal("ManualPayment.PayloadMismatch", await P0206Fixture.CodeAsync(changed));
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner,
            "UPDATE admin_identities SET deactivated_at = mfa_enrolled_at WHERE email = 'MANUAL.BILLING@EXAMPLE.TEST'");
        Assert.Equal(1L, await fixture.CountAsync("admin_identities", "email = 'MANUAL.BILLING@EXAMPLE.TEST' AND deactivated_at IS NOT NULL"));
        using var denied = await P0212Fixture.PostAsync(billing, P0306Tests.Path(purchase.Quote), command);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }
}
