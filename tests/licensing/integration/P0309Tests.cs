using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0308Tests;

namespace Intelibill.Licensing.Integration.Tests;

[Collection(MerchantBillingHosts.Name)]
public sealed partial class P0309Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task TrialConversionStartsAtReadyFulfilmentWithoutUnusedTrialDays()
    {
        await using var fixture = await StartAsync(database, March31);
        var package = await fixture.PackageAsync(1);
        var owner = package.Payer;
        await ApplyTrialAsync(fixture, owner);

        fixture.At(March31.AddDays(1));
        var quote = await AcceptAsync(package, await FirstQuoteAsync(package, "Month"));
        var attempt = await StartPaymentAsync(package, quote);
        await P0302Fixture.CaptureAsync(owner.Client, quote, attempt);

        var ready = March31.AddDays(2);
        fixture.At(ready);
        var paid = await P0302Fixture.VerifyAsync(owner.Client, quote, attempt);
        P0302Fixture.AssertStatus(paid, "Verified", "Fulfilled", "Applying");
        Assert.Equal(ready, paid.GetProperty("termStartsAt").GetDateTimeOffset());
        Assert.Equal(Utc("2027-05-02T04:30:00Z"), paid.GetProperty("termEndsAt").GetDateTimeOffset());
        Assert.Equal("Paid", (await ShopTermAsync(package, owner)).GetProperty("basis").GetString());
        Assert.Equal(1L, await fixture.CountAsync("trial_history", $"shop_id = '{owner.ShopId:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments", $"order_id = '{quote.OrderId:D}'"));
    }

    [Fact]
    public async Task PostRestrictionPurchaseStartsFreshAndDoesNotChargeRestrictedGap()
    {
        await using var fixture = await StartAsync(database, March31);
        var package = await fixture.PackageAsync(1);
        var (_, predecessor) = await PurchaseAsync(package, "Month");
        var original = await TermRowAsync(fixture, predecessor);
        fixture.At(April30.AddDays(7));
        var quote = await AcceptAsync(package, await RenewalQuoteAsync(package, predecessor, "Month"));
        Assert.Equal("RestoredAccess", quote.StartRule);
        Assert.Equal(0, quote.GraceDaysIncluded);
        var attempt = await StartPaymentAsync(package, quote);
        await P0302Fixture.CaptureAsync(package.Payer.Client, quote, attempt);

        var ready = April30.AddDays(10).AddTicks(10);
        fixture.At(ready);
        var paid = await P0302Fixture.VerifyAsync(package.Payer.Client, quote, attempt);
        P0302Fixture.AssertStatus(paid, "Verified", "Fulfilled", "Applying");
        Assert.Equal(Utc("2027-05-10T04:30:01Z"), paid.GetProperty("termStartsAt").GetDateTimeOffset());
        Assert.Equal(Utc("2027-06-10T04:30:01Z"), paid.GetProperty("termEndsAt").GetDateTimeOffset());
        fixture.At(ready.AddSeconds(1));
        Assert.Equal(Utc("2027-05-10T04:30:01Z"), (await ShopTermAsync(package, package.Payer)).GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(original, await TermRowAsync(fixture, predecessor));
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments", $"order_id = '{quote.OrderId:D}'"));
    }

    [Fact]
    public async Task PaymentDuringTradingSuspensionRecordsMoneyWithoutStartingTerm()
    {
        await using var fixture = await StartAsync(database, March31);
        var package = await fixture.PackageAsync(1);
        var quote = await AcceptAsync(package, await FirstQuoteAsync(package, "Month"));
        var attempt = await StartPaymentAsync(package, quote);
        await P0302Fixture.CaptureAsync(package.Payer.Client, quote, attempt);
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            UPDATE public.entitlement_account_heads SET revision = revision + 1, restriction = 'Trading'
            WHERE account_id = '{package.Account:D}'
            """);

        var paid = await P0302Fixture.VerifyAsync(package.Payer.Client, quote, attempt);
        P0302Fixture.AssertStatus(paid, "Verified", "NeedsReview", "NotApplicable");
        Assert.Equal(1L, await fixture.CountAsync("verified_payments", $"order_id = '{quote.OrderId:D}'"));
        Assert.Equal(0L, await fixture.CountAsync("payment_fulfilments", $"order_id = '{quote.OrderId:D}'"));
        Assert.Equal("Trading", await fixture.ScalarAsync($"""
            SELECT restriction FROM public.entitlement_account_heads WHERE account_id = '{package.Account:D}'
            """));

        fixture.At(March31.AddDays(5));
        var held = await P0302Fixture.VerifyAsync(package.Payer.Client, quote, attempt);
        P0302Fixture.AssertStatus(held, "Verified", "NeedsReview", "NotApplicable");
        var predicate = $"order_id = '{quote.OrderId:D}'";
        Assert.Equal(1L, await fixture.CountAsync("payment_restoration_holds", predicate));

        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            UPDATE public.entitlement_account_heads SET revision = revision + 1
            WHERE account_id = '{package.Account:D}'
            """);
        P0302Fixture.AssertStatus(await P0302Fixture.VerifyAsync(package.Payer.Client, quote, attempt),
            "Verified", "NeedsReview", "NotApplicable");

        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            UPDATE public.entitlement_account_heads SET revision = revision + 1, released_revision = revision,
                restriction = 'Unrestricted' WHERE account_id = '{package.Account:D}'
            """);
        var retried = await Task.WhenAll(Enumerable.Range(0, 2)
            .Select(_ => P0302Fixture.VerifyAsync(package.Payer.Client, quote, attempt)));
        foreach (var result in retried)
        {
            P0302Fixture.AssertStatus(result, "Verified", "Fulfilled", "Applying");
            Assert.Equal(March31.AddDays(5), result.GetProperty("termStartsAt").GetDateTimeOffset());
        }
        Assert.Equal(1L, await fixture.CountAsync("verified_payments", predicate));
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments", predicate));
        Assert.Equal(1L, await fixture.CountAsync("paid_fresh_starts", $"{predicate} AND released_revision IS NOT NULL"));
    }
}
