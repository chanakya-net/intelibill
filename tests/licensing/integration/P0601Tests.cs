using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// P06-01: an owner previews a prorated capacity increase for the remaining calendar seconds of the current paid term.
/// The purchased gross, anchor and paid-through stay frozen; a later published price only prices the target.
/// Real PostgreSQL, real merchant and licensing APIs, the local simulator purchase and fixed clocks; no real provider.
/// </summary>
public sealed partial class P0601Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    // 31 January 2028, 10:00 IST; 2028 is a leap year, so the first monthly period ends 29 February 10:00 IST.
    internal static readonly DateTimeOffset January31 = P0308Tests.Utc("2028-01-31T04:30:00Z");
    internal static readonly DateTimeOffset February29 = P0308Tests.Utc("2028-02-29T04:30:00Z");
    internal static readonly DateTimeOffset February14 = P0308Tests.Utc("2028-02-14T04:30:00Z");
    internal static readonly string[] ShopNames = ["Leap Central", "Leap East"];

    [Fact]
    public async Task OwnerQuotesLeapMonthIncreaseFromPurchasedGross()
    {
        await using var fixture = await StartAsync(database, January31);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (purchase, term) = await PurchaseAsync(package, "Month");
        Assert.Equal(12000L, purchase.AmountPaise);
        Assert.Equal(3, purchase.Capacity);
        var visible = await AccountTermAsync(package, term);
        Assert.Equal(February29, visible.GetProperty("paidThrough").GetDateTimeOffset());
        var baseline = await EvidenceAsync(fixture, term);

        var successor = await RepriceAsync(fixture, package, 18000L);
        fixture.At(February14);
        var request = Request(successor, 4, 3, February29);
        using var response = await PostQuoteAsync(package, term, request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        Assert.True(response.Headers.CacheControl?.NoStore);
        var quote = JsonDocument.Parse(body).RootElement.Clone();
        AssertLeapQuote(quote, term, successor);

        var shops = await ShopsAsync(package, term, quote.GetProperty("quoteId").GetGuid());
        Assert.Equal(ShopNames.Order(), shops.Order());
        using var replay = await PostQuoteAsync(package, term, request);
        var replayed = JsonDocument.Parse(await replay.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(quote.GetProperty("quoteId").GetGuid(), replayed.GetProperty("quoteId").GetGuid());
        Assert.Equal(quote.GetProperty("snapshotHash").GetString(), replayed.GetProperty("snapshotHash").GetString());

        Assert.Equal(baseline, await EvidenceAsync(fixture, term));
        Assert.Equal(1L, await fixture.CountAsync("capacity_change_quotes"));
        Assert.Equal(2L, await fixture.CountAsync("capacity_change_quote_shops"));
    }

    private static void AssertLeapQuote(JsonElement quote, Guid term, Guid successor)
    {
        Assert.Equal("Payable", quote.GetProperty("outcome").GetString());
        Assert.Equal(term, quote.GetProperty("termId").GetGuid());
        Assert.Equal(successor, quote.GetProperty("targetPlanVersionId").GetGuid());
        Assert.Equal(3, quote.GetProperty("priorCapacity").GetInt32());
        Assert.Equal(4, quote.GetProperty("targetCapacity").GetInt32());
        Assert.Equal(2, quote.GetProperty("shopCount").GetInt32());
        Assert.Equal(12000L, quote.GetProperty("purchasedGrossPaise").GetInt64());
        Assert.Equal(33000L, quote.GetProperty("targetGrossPaise").GetInt64());
        Assert.Equal(21000L, quote.GetProperty("differencePaise").GetInt64());
        Assert.Equal(2_505_600L, quote.GetProperty("totalSeconds").GetInt64());
        Assert.Equal(1_296_000L, quote.GetProperty("remainingSeconds").GetInt64());
        Assert.Equal(10862L, quote.GetProperty("amountPaise").GetInt64());
        Assert.Equal(January31, quote.GetProperty("intervalStartsAt").GetDateTimeOffset());
        Assert.Equal(February29, quote.GetProperty("paidThrough").GetDateTimeOffset());
        Assert.Equal("2028-01-31T10:00:00", quote.GetProperty("anchorLocal").GetString());
        Assert.Equal(February14, quote.GetProperty("evaluatedAt").GetDateTimeOffset());
        Assert.Equal(February14.AddMinutes(15), quote.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.Equal("proration/1", quote.GetProperty("prorationVersion").GetString());
    }

    internal static object Request(Guid target, int capacity, int observed, DateTimeOffset paidThrough, Guid? key = null) => new
    {
        operationKey = key ?? Guid.NewGuid(), targetPlanVersionId = target, targetCapacity = capacity,
        observedCapacity = observed, observedPaidThrough = paidThrough,
    };

    internal static string QuotesPath(Guid account, Guid term) =>
        $"/api/billing/accounts/{account:D}/subscriptions/{term:D}/capacity-change-quotes";

    internal static Task<HttpResponseMessage> PostQuoteAsync(Package package, Guid term, object request) =>
        package.Payer.Client.PostAsJsonAsync(QuotesPath(package.Account, term), request);

    internal static async Task<string[]> ShopsAsync(Package package, Guid term, Guid quote)
    {
        var page = await package.Payer.Client.GetFromJsonAsync<JsonElement>(
            $"{QuotesPath(package.Account, term)}/{quote:D}/shops");
        Assert.Equal(JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);
        return [.. page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!)];
    }

    internal static async Task<JsonElement> AccountTermAsync(Package package, Guid term)
    {
        var status = await P0209Fixture.ReadAsync(await package.Payer.Client.GetAsync(
            new Uri($"/api/billing/accounts/{package.Account:D}/subscriptions", UriKind.Relative)));
        return status.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("status"))
            .Single(item => item.GetProperty("termId").GetGuid() == term);
    }

    /// <summary>Publishes a successor of the purchased plan at a higher monthly base price.</summary>
    internal static async Task<Guid> RepriceAsync(P0308Fixture fixture, Package package, long monthlyPricePaise)
    {
        var plan = (Guid)(await fixture.ScalarAsync($"SELECT plan_id FROM plan_versions WHERE id = '{package.Plan:D}'"))!;
        return await P0301Tests.PublishPlanAsync(fixture.Checkout.Trial, plan, package.Plan, monthlyPricePaise: monthlyPricePaise);
    }

    /// <summary>Paid term, price, coverage and every payment artefact that a preview must never change.</summary>
    internal static async Task<string> EvidenceAsync(P0308Fixture fixture, Guid term) => string.Join('|',
        await P0308Tests.TermRowAsync(fixture, term),
        await fixture.ScalarAsync($"""
            SELECT price_paise || ' ' || currency || ' ' || plan_version_id || ' ' || member_allowance || ' ' || t.capacity
            FROM subscription_term_facts f JOIN subscription_coverage_terms t USING (account_id, term_id)
            WHERE term_id = '{term:D}'
            """),
        await fixture.CountAsync("checkout_orders"), await fixture.CountAsync("payment_attempts"),
        await fixture.CountAsync("verified_payments"), await fixture.CountAsync("payment_fulfilments"),
        await fixture.CountAsync("payment_fulfilment_shops"), await fixture.CountAsync("subscription_coverage_terms"),
        await fixture.CountAsync("shop_affiliations"), await fixture.CountAsync("entitlement_outbox"));

    internal static string Rupees(long paise) =>
        "₹" + (paise / 100m).ToString("N2", CultureInfo.GetCultureInfo("en-IN"));
}
