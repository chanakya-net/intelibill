using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0602Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task PayableQuoteCreatesOneBoundOrder()
    {
        await using var fixture = await StartAsync(database, January31);
        var (package, term, path, request) = await PreviewAsync(fixture);
        using var converted = await package.Payer.Client.PostAsJsonAsync(path, request);
        Assert.True(converted.IsSuccessStatusCode, $"Convert reviewed quote returned {(int)converted.StatusCode}");
        var order = await converted.Content.ReadFromJsonAsync<CheckoutQuoteV1>();
        Assert.Equal(term, order!.TermId);
        Assert.Equal(10862L, order.AmountPaise);
        Assert.Equal(February29, order.PreviewEndsAt);
        Assert.Equal(February14.AddMinutes(15), order.ExpiresAt);
        using var replay = await package.Payer.Client.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(order.OrderId, (await replay.Content.ReadFromJsonAsync<CheckoutQuoteV1>())!.OrderId);
        Assert.Equal(2L, await fixture.CountAsync("checkout_orders"));
        Assert.Equal(1L, await fixture.CountAsync("verified_payments"));
    }

    [Fact]
    public async Task FullPaymentQueuesOneIncreaseWithoutCreatingATerm()
    {
        await using var fixture = await StartAsync(database, January31);
        var (package, term, path, request) = await PreviewAsync(fixture);
        using var converted = await package.Payer.Client.PostAsJsonAsync(path, request);
        converted.EnsureSuccessStatusCode();
        var order = await AcceptAsync(package, (await converted.Content.ReadFromJsonAsync<CheckoutQuoteV1>())!);
        var attempt = await StartPaymentAsync(package, order);
        var accepted = await P0302Fixture.StatusAsync(package.Payer.Client, order);
        Assert.Equal("AwaitingPayment", accepted.GetProperty("payment").GetString());
        Assert.Equal(0L, await fixture.CountAsync("verified_payments", $"order_id = '{order.OrderId:D}'"));
        await P0302Fixture.CaptureAsync(package.Payer.Client, order, attempt);
        var paid = await P0302Fixture.VerifyAsync(package.Payer.Client, order, attempt);
        P0302Fixture.AssertStatus(paid, "Verified", "Pending", "NotApplicable");
        await P0302Fixture.VerifyAsync(package.Payer.Client, order, attempt);
        Assert.Equal(1L, await fixture.CountAsync("verified_payments", $"order_id = '{order.OrderId:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("payment_confirmations", $"order_id = '{order.OrderId:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("entitlement_messages", "kind = 'CapacityReserve'"));
        Assert.Equal(1L, await fixture.CountAsync("subscription_coverage_terms"));
        Assert.Equal(February29, (await AccountTermAsync(package, term)).GetProperty("paidThrough").GetDateTimeOffset());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReservationDecisionSurvivesReplayAndKeepsOriginalAnchor(bool expireAtBoundary)
    {
        await using var fixture = await StartAsync(database, January31);
        var (package, term, path, request) = await PreviewAsync(fixture);
        using var converted = await package.Payer.Client.PostAsJsonAsync(path, request);
        converted.EnsureSuccessStatusCode();
        var order = await AcceptAsync(package, (await converted.Content.ReadFromJsonAsync<CheckoutQuoteV1>())!);
        var delivery = fixture.Checkout.Delivery;
        await delivery.ScanAsync(true);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        var original = await P0308Tests.TermRowAsync(fixture, term);
        await PayAsync(package, order);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        Assert.Equal(1L, await fixture.CountAsync("entitlement_messages", "kind = 'CapacityConsume'"));
        if (expireAtBoundary) fixture.At(February14.AddMinutes(5));
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        var paid = await P0302Fixture.StatusAsync(package.Payer.Client, order);
        P0302Fixture.AssertStatus(paid, "Verified", expireAtBoundary ? "NeedsReview" : "Fulfilled",
            expireAtBoundary ? "NotApplicable" : "Applied");
        Assert.False(paid.GetProperty("canStart").GetBoolean());
        var status = await AccountTermAsync(package, term);
        Assert.Equal(expireAtBoundary ? 3 : 4, status.GetProperty("purchasedCapacity").GetInt32());
        Assert.Equal(February29, status.GetProperty("paidThrough").GetDateTimeOffset());
        Assert.Equal(original, await P0308Tests.TermRowAsync(fixture, term));
        Assert.Equal(1L, await fixture.CountAsync("payment_confirmations", $"order_id='{order.OrderId:D}'"));
    }

    private static async Task<(Package Package, Guid Term, string Path, object Request)> PreviewAsync(P0308Fixture fixture)
    {
        var package = await fixture.PackageAsync(2, ShopNames);
        var (_, term) = await PurchaseAsync(package, "Month");
        var successor = await RepriceAsync(fixture, package, 18000);
        fixture.At(February14);
        using var quoted = await PostQuoteAsync(package, term, Request(successor, 4, 3, February29));
        quoted.EnsureSuccessStatusCode();
        var quote = await quoted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Payable", quote.GetProperty("outcome").GetString());
        var path = $"{QuotesPath(package.Account, term)}/{quote.GetProperty("quoteId").GetGuid():D}/order";
        var request = new { operationKey = Guid.NewGuid(), snapshotHash = quote.GetProperty("snapshotHash").GetString() };
        return (package, term, path, request);
    }
}
