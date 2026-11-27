using System.Net.Http.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0312Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedAnchoredRenewalKeepsAcceptedFactsThroughAcknowledgement(bool grace)
    {
        await using var renewal = await P0308Fixture.StartAsync(database, P0308Tests.March31);
        var package = await renewal.PackageAsync(3, ["Renewal One", "Renewal Two", "Renewal Three"]);
        var (_, predecessor) = await PurchaseAsync(package, "Month");
        await renewal.Checkout.Delivery.DrainAsync(source: true);
        await renewal.Checkout.Delivery.DrainAsync(source: false);
        var original = await P0308Tests.TermRowAsync(renewal, predecessor);
        var effective = grace ? P0308Tests.April30.AddDays(2).AddHours(3) : P0308Tests.April30.AddDays(-10);
        renewal.At(effective);
        var quote = await AcceptAsync(package, await RenewalQuoteAsync(package, predecessor, "Month"));
        var purchase = new Purchase(package.Payer, package.Account, package.Shops, quote);
        var attempt = await StartPaymentAsync(package, quote);
        await CaptureAsync(package.Payer.Client, quote, attempt);
        renewal.At(grace ? P0308Tests.April30.AddDays(10) : effective.AddDays(2));
        Assert.True(renewal.Checkout.CheckoutClock.Now > quote.ExpiresAt);
        await VerifyAsync(package.Payer.Client, quote, attempt);
        await AssertRenewalAsync(renewal, purchase, predecessor, original, effective, grace);
        var receipts = await ConfirmationsAsync(purchase);
        await VerifyAsync(package.Payer.Client, quote, attempt);
        await AssertPackageAsync(renewal.Checkout, purchase);
        await ObserveAsync(renewal.Checkout, purchase, $"renewal-grace-{grace}-applying", new { predecessor, effective, quote.SourceHash, quote.GraceDaysIncluded });
        await renewal.Checkout.Delivery.DrainAsync(source: true);
        await renewal.Checkout.Delivery.DrainAsync(source: false);
        await AssertDeliveryAsync(renewal.Checkout, purchase, 3);
        await AssertSameConfirmationsAsync(purchase, receipts);
        await ObserveAsync(renewal.Checkout, purchase, $"renewal-grace-{grace}-applied");
    }

    private static async Task AssertRenewalAsync(P0308Fixture renewal, Purchase purchase, Guid predecessor,
        string original, DateTimeOffset effective, bool grace)
    {
        var quote = purchase.Quote;
        var status = await StatusAsync(purchase.Payer.Client, quote);
        AssertStatus(status, "Verified", "Fulfilled", "Applying");
        Assert.Equal(P0308Tests.April30, status.GetProperty("termStartsAt").GetDateTimeOffset());
        Assert.Equal(P0308Tests.May31, status.GetProperty("termEndsAt").GetDateTimeOffset());
        Assert.Equal("2027-03-31T10:00:00", quote.AnchorLocal);
        Assert.Equal(grace ? 3 : 0, quote.GraceDaysIncluded);
        Assert.Equal(original, await P0308Tests.TermRowAsync(renewal, predecessor));
        Assert.Equal($"{P0308Tests.Iso(effective)}|{(grace ? 3 : 0)}|renewal/1|{predecessor:D}", await P0308Tests.ProvenanceAsync(renewal, quote));
        var saved = await purchase.Payer.Client.GetFromJsonAsync<CheckoutQuoteV1>(P0301Tests.OrdersPath(quote.AccountId) + $"/{quote.OrderId:D}");
        Assert.Equal(quote, saved);
        var receipt = Assert.Single(await ConfirmationsAsync(purchase));
        var facts = await FactsAsync(renewal.Checkout, receipt.Receipt.ConfirmationId);
        Assert.Equal((quote.AmountPaise, quote.Currency, quote.PlanVersionId, quote.SnapshotHash),
            (facts.Receipt.AmountPaise, facts.Receipt.Currency, facts.Terms.PlanVersionId, facts.Terms.SnapshotHash));
        Assert.Equal(PackageIds(purchase), facts.Terms.Shops.Select(s => s.ShopId).Order().ToArray());
        Assert.Equal(effective, facts.EffectiveAt);
        Assert.Equal(renewal.Checkout.CheckoutClock.Now, facts.Receipt.VerifiedAt);
        Assert.Equal(facts.Receipt.VerifiedAt, facts.Receipt.IssuedAt);
        Assert.True(facts.EffectiveAt < facts.Receipt.VerifiedAt);
    }

    private static Guid[] PackageIds(Purchase purchase) => purchase.Shops.Select(s => s.ShopId).Order().ToArray();
}
