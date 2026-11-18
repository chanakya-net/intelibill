using System.Globalization;
using System.Net;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Domain.Policies;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// P03-08: a full renewal bought before expiry or inside eligible grace extends from the prior paid-through end on
/// the original calendar anchor. Real PostgreSQL, the local simulator and fixed clocks; no real provider.
/// </summary>
public sealed partial class P0308Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    // 31 March 2027, 10:00 IST: a month-end anchor that clips to 30 April and returns to 31 May.
    internal static readonly DateTimeOffset March31 = Utc("2027-03-31T04:30:00Z");
    internal static readonly DateTimeOffset April30 = Utc("2027-04-30T04:30:00Z");
    internal static readonly DateTimeOffset May31 = Utc("2027-05-31T04:30:00Z");
    internal static readonly DateTimeOffset June30 = Utc("2027-06-30T04:30:00Z");

    [Fact]
    public async Task EarlyRenewalKeepsMonthEndAnchorAndLosesNoPaidTime()
    {
        await using var fixture = await StartAsync(database, March31);
        var package = await fixture.PackageAsync(2);
        var (_, first) = await PurchaseAsync(package, "Month");
        await fixture.Checkout.Delivery.DrainAsync(source: true);
        await fixture.Checkout.Delivery.DrainAsync(source: false);
        var original = await TermRowAsync(fixture, first);

        fixture.At(Utc("2027-04-20T06:00:00Z"));
        var quote = await RenewalQuoteAsync(package, first, "Month");
        Assert.Equal("PriorPaidThrough", quote.StartRule);
        Assert.Equal(April30, quote.PreviewStartsAt);
        Assert.Equal(May31, quote.PreviewEndsAt);
        Assert.Equal(0, quote.GraceDaysIncluded);
        Assert.Equal("2027-03-31T10:00:00", quote.AnchorLocal);
        Assert.Equal(2, quote.ShopCount);

        var accepted = await AcceptAsync(package, quote);
        var paid = await PayAsync(package, accepted);
        P0302Fixture.AssertStatus(paid, "Verified", "Fulfilled", "Applying");
        var renewed = paid.GetProperty("termId").GetGuid();
        Assert.NotEqual(first, renewed);
        Assert.Equal(April30, paid.GetProperty("termStartsAt").GetDateTimeOffset());
        Assert.Equal(May31, paid.GetProperty("termEndsAt").GetDateTimeOffset());
        Assert.Equal(original, await TermRowAsync(fixture, first));
        Assert.Equal($"{Iso(April30)}|{Iso(May31)}|2027-03-31T10:00:00|1|Paid", await TermRowAsync(fixture, renewed));

        foreach (var shop in package.Shops)
        {
            var term = await ShopTermAsync(package, shop);
            Assert.Equal("Active", term.GetProperty("state").GetString());
            Assert.Equal(March31, term.GetProperty("startsAt").GetDateTimeOffset());
            Assert.Equal(May31, term.GetProperty("paidThrough").GetDateTimeOffset());
            Assert.Equal(May31.AddDays(7), term.GetProperty("graceEndsAt").GetDateTimeOffset());
        }

        await fixture.Checkout.Delivery.DrainAsync(source: true);
        await fixture.Checkout.Delivery.DrainAsync(source: false);
        P0302Fixture.AssertStatus(await P0302Fixture.StatusAsync(package.Payer.Client, accepted), "Verified", "Fulfilled", "Applied");

        // The superseded period can never be renewed again; the chain tail renews again from 31 May.
        using var stale = await TryRenewalQuoteAsync(package, first, "Month");
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        fixture.At(Utc("2027-05-20T06:00:00Z"));
        var (next, again) = await RenewAsync(package, renewed, "Month");
        Assert.Equal(May31, next.PreviewStartsAt);
        Assert.Equal(June30, again.GetProperty("termEndsAt").GetDateTimeOffset());
        Assert.Equal(June30, (await ShopTermAsync(package, package.Payer)).GetProperty("paidThrough").GetDateTimeOffset());
        Assert.Equal(3L, await fixture.CountAsync("payment_fulfilments"));
        Assert.Equal(6L, await fixture.CountAsync("payment_fulfilment_shops"));
    }

    [Fact]
    public async Task ImportedPaidTermRenewsFromItsV1SourceOnTheOriginalAnchor()
    {
        var anchor = new BillingAnchor(new DateTime(2027, 1, 31, 10, 0, 0), BillingCalendar.Zone, BillingPeriod.Month);
        var (starts, ends) = BillingCalendar.Period(anchor, 0);
        await using var fixture = await StartAsync(database, Utc("2027-02-20T06:00:00Z"));
        var package = await fixture.PackageAsync(1);
        var imported = await P0209Fixture.SeedTermAsync(fixture.Hosts, package.Account, new(starts, ends,
            AnchorLocal: "2027-01-31 10:00:00", Grouped: true, Capacity: 3));
        var shop = Guid.NewGuid();
        await P0209Fixture.AffiliateAsync(fixture.Hosts, package.Account, package.Payer.UserId, shop, package.Payer.UserId,
            imported, starts, ends);
        var original = await TermRowAsync(fixture, imported);
        var originalAffiliation = $"SELECT to_jsonb(a)::text FROM shop_affiliations a WHERE shop_id = '{shop:D}'";
        var affiliation = await fixture.ScalarAsync(originalAffiliation);
        var digest = new CheckoutSource([new(shop, "Status Fixture Shop",
            (Guid)(await fixture.ScalarAsync($"SELECT request_id FROM shop_affiliations WHERE shop_id = '{shop:D}'"))!,
            imported, starts, ends)], new(starts, ends, 3, "Paid", "Month", anchor.LocalStart, 0, BillingCalendar.Version,
            GracePolicy.Version, 0)).Hash();

        var quote = await RenewalQuoteAsync(package, imported, "Month");
        Assert.Equal(digest, quote.SourceHash);
        Assert.Equal(ends, quote.PreviewStartsAt);
        Assert.Equal(BillingCalendar.Boundary(anchor, 2), quote.PreviewEndsAt);
        var accepted = await AcceptAsync(package, quote);
        var paid = await PayAsync(package, accepted);
        P0302Fixture.AssertStatus(paid, "Verified", "Fulfilled", "Applying");
        var renewed = paid.GetProperty("termId").GetGuid();
        Assert.Equal($"{Iso(ends)}|{Iso(BillingCalendar.Boundary(anchor, 2))}|2027-01-31T10:00:00|1|Paid",
            await TermRowAsync(fixture, renewed));
        Assert.Equal(imported, await fixture.ScalarAsync($"SELECT term_id FROM shop_affiliations WHERE shop_id = '{shop:D}'"));
        Assert.Equal(renewed, await fixture.ScalarAsync($"SELECT term_id FROM payment_fulfilment_shops WHERE shop_id = '{shop:D}'"));
        using var stale = await TryRenewalQuoteAsync(package, imported, "Month");
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);

        fixture.At(Utc("2027-03-20T06:00:00Z"));
        var (next, paidAgain) = await RenewAsync(package, renewed, "Month");
        P0302Fixture.AssertStatus(paidAgain, "Verified", "Fulfilled", "Applying");
        var second = paidAgain.GetProperty("termId").GetGuid();
        var march = BillingCalendar.Boundary(anchor, 2);
        var april = BillingCalendar.Boundary(anchor, 3);
        Assert.Equal(march, next.PreviewStartsAt);
        Assert.Equal(april, next.PreviewEndsAt);
        Assert.Equal($"{Iso(march)}|{Iso(april)}|2027-01-31T10:00:00|2|Paid", await TermRowAsync(fixture, second));
        Assert.Equal(original, await TermRowAsync(fixture, imported));
        Assert.Equal(affiliation, await fixture.ScalarAsync(originalAffiliation));
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments",
            $"order_id = '{accepted.OrderId:D}' AND predecessor_term_id = '{imported:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments", $"predecessor_term_id = '{renewed:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments", $"order_id = '{next.OrderId:D}'"));
        Assert.Equal(2L, await fixture.CountAsync("verified_payments"));
    }

    internal static async Task<string> TermRowAsync(P0308Fixture fixture, Guid term) =>
        (string)(await fixture.ScalarAsync($"""
            SELECT to_char(t.starts_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"+00:00"') || '|' ||
                to_char(t.ends_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"+00:00"') || '|' ||
                to_char(f.anchor_local, 'YYYY-MM-DD"T"HH24:MI:SS') || '|' || f.period_ordinal || '|' || f.basis
            FROM subscription_coverage_terms t JOIN subscription_term_facts f USING (account_id, term_id)
            WHERE t.term_id = '{term:D}'
            """))!;

    internal static string Iso(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", CultureInfo.InvariantCulture);

    internal static DateTimeOffset Utc(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
}
