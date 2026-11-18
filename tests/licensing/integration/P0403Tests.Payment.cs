using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;
using static Intelibill.Licensing.Integration.Tests.P0308Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0403Tests
{
    [Theory]
    [InlineData("Security", false)]
    [InlineData("Trading", false)]
    [InlineData("Security", true)]
    public async Task PaymentCannotLiftARecordedAccountSuspension(string mode, bool manual)
    {
        await using var fixture = await P0308Fixture.StartAsync(database, March31, P0306Tests.Settings);
        var package = await fixture.PackageAsync(1);
        var quote = await P0308Fixture.AcceptAsync(package, await P0308Fixture.FirstQuoteAsync(package, "Month"));
        var delivery = fixture.Checkout.Delivery;
        using var security = await SignInSecurityAsync(delivery, "billing");
        await ApplySuspensionAsync(security, package.Account, "Account", null, mode);
        var suspended = await delivery.LatestSnapshotAsync(package.Payer.ShopId);

        if (manual)
        {
            using var receipt = await P0212Fixture.PostAsync(security, P0306Tests.Path(quote), P0306Tests.Receipt(quote, March31));
            Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);
        }
        else
        {
            await P0308Fixture.PayAsync(package, quote);
        }

        Assert.Equal(0L, await fixture.Checkout.CountAsync("payment_fulfilments"));
        Assert.Equal(1L, await fixture.Checkout.CountAsync("payment_restoration_holds", $"restriction = '{mode}'"));
        Assert.Equal(mode, await delivery.SourceScalarAsync(
            $"SELECT restriction FROM entitlement_account_heads WHERE account_id = '{package.Account:D}'"));
        var latest = await delivery.LatestSnapshotAsync(package.Payer.ShopId);
        Assert.Equal(suspended.Account, latest.Account);
        Assert.Equal(mode, latest.Account!.Suspension?.Mode);
    }

    [Fact]
    public async Task FulfilledPaymentKeepsAShopSuspensionInItsNewSnapshot()
    {
        await using var fixture = await P0308Fixture.StartAsync(database, March31, P0306Tests.Settings);
        var package = await fixture.PackageAsync(1);
        var quote = await P0308Fixture.AcceptAsync(package, await P0308Fixture.FirstQuoteAsync(package, "Month"));
        var delivery = fixture.Checkout.Delivery;
        using var security = await SignInSecurityAsync(delivery);
        var shop = package.Payer.ShopId;
        await ApplySuspensionAsync(security, package.Account, "Shop", shop, "Security");
        var suspended = await delivery.LatestSnapshotAsync(shop);

        AssertStatus(await P0308Fixture.PayAsync(package, quote), "Verified", "Fulfilled", "Applying");

        Assert.Equal(1L, await fixture.Checkout.CountAsync("payment_fulfilments"));
        var latest = await delivery.LatestSnapshotAsync(shop);
        Assert.True(latest.Shop!.Revision > suspended.Shop!.Revision);
        Assert.NotNull(suspended.Shop.Suspension);
        Assert.Equal(suspended.Shop.Suspension, latest.Shop.Suspension);
        Assert.Equal("Unrestricted", latest.Account!.Restriction);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task FreshPaymentAtScheduledSuspensionBoundary(int microseconds, bool manual)
    {
        await using var fixture = await P0308Fixture.StartAsync(database, March31, P0306Tests.Settings);
        var package = await fixture.PackageAsync(1);
        var quote = await P0308Fixture.AcceptAsync(package, await P0308Fixture.FirstQuoteAsync(package, "Month"));
        var delivery = fixture.Checkout.Delivery;
        using var security = await SignInSecurityAsync(delivery, "billing");
        var effectiveAt = March31.AddSeconds(5);
        await ApplySuspensionAsync(security, package.Account, "Account", null, "Trading", effectiveAt);
        var at = effectiveAt.AddMicroseconds(microseconds);
        var attempt = manual ? (Guid?)null : await P0308Fixture.StartPaymentAsync(package, quote);
        fixture.At(at);
        if (attempt is { } id)
        {
            await CaptureAsync(package.Payer.Client, quote, id);
            var verified = await VerifyAsync(package.Payer.Client, quote, id);
            Assert.Equal("Verified", verified.GetProperty("payment").GetString());
        }
        else
        {
            using var receipt = await P0212Fixture.PostAsync(security, P0306Tests.Path(quote), P0306Tests.Receipt(quote, at));
            Assert.True(receipt.StatusCode == HttpStatusCode.Created, await receipt.Content.ReadAsStringAsync());
        }

        var held = microseconds >= 0;
        Assert.Equal(1L, await fixture.Checkout.CountAsync(manual ? "manual_payment_receipts" : "verified_payments"));
        Assert.Equal(held ? 0L : 1L, await fixture.Checkout.CountAsync("payment_fulfilments"));
        Assert.Equal(held ? 1L : 0L, await fixture.Checkout.CountAsync("payment_restoration_holds", "restriction = 'Trading'"));
        var latest = await delivery.LatestSnapshotAsync(package.Payer.ShopId);
        Assert.Equal("Trading", latest.Account!.Restriction);
        Assert.Equal(effectiveAt, latest.Account.Suspension?.EffectiveAt);
    }

    private static async Task<Guid> ApplySuspensionAsync(P0110CookieBrowser security, Guid account, string scope, Guid? shopId, string mode,
        DateTimeOffset? effectiveAt = null)
    {
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        using var preview = await P0212Fixture.PostAsync(security, path + "/preview", new { scope, shopId });
        Assert.True(preview.StatusCode == HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        var impact = await preview.Content.ReadFromJsonAsync<JsonElement>();
        using var saved = await P0212Fixture.PostAsync(security, path, new
        {
            scope, shopId, mode, operationKey = Guid.NewGuid(), expectedVersion = 0,
            impactDigest = impact.GetProperty("impactDigest").GetString(),
            customerExplanation = "Contact support about shop access.",
            internalReason = "Internal review detail.", reviewReference = "CASE-878", effectiveAt,
        });
        Assert.True(saved.StatusCode == HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        return (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
