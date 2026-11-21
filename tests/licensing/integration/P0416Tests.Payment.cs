namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0416Tests
{
    [Fact]
    public async Task ExplicitFreshPaymentDoesNotRestoreTheClosedShop()
    {
        var clock = new DateTimeOffset(2027, 3, 31, 4, 30, 0, TimeSpan.Zero);
        await using var fixture = await P0308Fixture.StartAsync(database, clock, P0306Tests.Settings);
        var package = await fixture.PackageAsync(1);
        var quote = await P0308Fixture.AcceptAsync(package, await P0308Fixture.FirstQuoteAsync(package, "Month"));
        using var closed = await ConfirmShopAsync(package.Payer);
        Assert.True(closed.IsSuccessStatusCode, await closed.Content.ReadAsStringAsync());

        var paid = await P0308Fixture.PayAsync(package, quote);
        P0302Fixture.AssertStatus(paid, "Verified", "Fulfilled", "NeedsReview");
        Assert.Equal(1L, await fixture.Checkout.CountAsync("payment_fulfilments"));
        var snapshot = await fixture.Checkout.Delivery.LatestSnapshotAsync(package.Payer.ShopId);
        Assert.Equal("Closed", snapshot.Shop?.State);
        Assert.Equal(paid.GetProperty("termId").GetGuid(), snapshot.Shop?.TermId);
        Assert.Equal("Unrestricted", snapshot.Account?.Restriction);
    }
}
