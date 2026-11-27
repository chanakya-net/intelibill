using System.Net;
using System.Net.Http.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0308Tests
{
    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    public async Task GraceRenewalVerifiedLateUsesCaptureTimeAndAddsGraceOnce(int observedAfterDays)
    {
        await using var fixture = await StartAsync(database, March31);
        var package = await fixture.PackageAsync(1);
        var (_, first) = await PurchaseAsync(package, "Month");

        var paidAt = April30.AddDays(2).AddHours(3);
        fixture.At(paidAt);
        Assert.Equal("Grace", (await ShopTermAsync(package, package.Payer)).GetProperty("state").GetString());
        var quote = await AcceptAsync(package, await RenewalQuoteAsync(package, first, "Month"));
        Assert.Equal(April30, quote.PreviewStartsAt);
        Assert.Equal(May31, quote.PreviewEndsAt);
        Assert.Equal(3, quote.GraceDaysIncluded);
        var attempt = await StartPaymentAsync(package, quote);
        await P0302Fixture.CaptureAsync(package.Payer.Client, quote, attempt);

        // Proof is observed after the quote ended (5 days: still in grace; 10 days: grace ended too). Grace days are
        // counted to the verified capture time, never the observation time, and the period is the accepted quote's.
        var observedAt = April30.AddDays(observedAfterDays);
        fixture.At(observedAt);
        var paid = await P0302Fixture.VerifyAsync(package.Payer.Client, quote, attempt);
        P0302Fixture.AssertStatus(paid, "Verified", "Fulfilled", "Applying");
        Assert.Equal(April30, paid.GetProperty("termStartsAt").GetDateTimeOffset());
        Assert.Equal(May31, paid.GetProperty("termEndsAt").GetDateTimeOffset());
        Assert.Equal($"{Iso(paidAt)}|3|renewal/1|{first:D}", await ProvenanceAsync(fixture, quote));

        var term = await ShopTermAsync(package, package.Payer);
        Assert.Equal("Active", term.GetProperty("state").GetString());
        Assert.Equal(March31, term.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(May31, term.GetProperty("paidThrough").GetDateTimeOffset());
        Assert.Equal(May31.AddDays(7), term.GetProperty("graceEndsAt").GetDateTimeOffset());
        fixture.At(May31);
        Assert.Equal("Grace", (await ShopTermAsync(package, package.Payer)).GetProperty("state").GetString());
        fixture.At(May31.AddDays(7));
        Assert.Equal("Expired", (await ShopTermAsync(package, package.Payer)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task RenewalWindowClosesAtExactGraceEndIndependentlyOfQuoteLifetime()
    {
        await using var fixture = await StartAsync(database, March31);
        var package = await fixture.PackageAsync(1);
        var (_, first) = await PurchaseAsync(package, "Month");
        var graceEnd = April30.AddDays(7);

        fixture.At(graceEnd.AddMinutes(-5));
        var late = await RenewalQuoteAsync(package, first, "Month");
        Assert.Equal(7, late.GraceDaysIncluded);
        fixture.At(graceEnd);
        Assert.True(graceEnd < late.ExpiresAt);
        using (var refused = await package.Payer.Client.PostAsJsonAsync(P0301Tests.AcceptPath(late), P0301Tests.Accept(late)))
            Assert.Equal(CheckoutOrdersV1.Expired, await P0206Fixture.CodeAsync(refused));

        fixture.At(graceEnd.AddMinutes(-10));
        var onTime = await RenewalQuoteAsync(package, first, "Month");
        fixture.At(graceEnd.AddTicks(-10));
        onTime = await AcceptAsync(package, onTime);
        fixture.At(graceEnd);
        Assert.False((await P0302Fixture.StatusAsync(package.Payer.Client, onTime)).GetProperty("canStart").GetBoolean());
        var start = await P0302Fixture.StartAsync(package.Payer.Client, onTime, HttpStatusCode.Conflict);
        Assert.Equal(CheckoutOrdersV1.Expired, start.GetProperty("code").GetString());
        Assert.Equal(0L, await fixture.CountAsync("payment_attempts", $"order_id = '{onTime.OrderId:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments"));
    }

    [Fact]
    public async Task AnnualLeapDayAnchorClipsInCommonYearsAndReturnsToFebruary29()
    {
        // 29 February 2028, 10:00 IST.
        var leap = Utc("2028-02-29T04:30:00Z");
        await using var fixture = await StartAsync(database, leap);
        var package = await fixture.PackageAsync(1);
        var (_, term) = await PurchaseAsync(package, "Year");
        Assert.Equal($"{Iso(leap)}|{Iso(Utc("2029-02-28T04:30:00Z"))}|2028-02-29T10:00:00|0|Paid", await TermRowAsync(fixture, term));

        string[] ends = ["2030-02-28T04:30:00Z", "2031-02-28T04:30:00Z", "2032-02-29T04:30:00Z"];
        for (var year = 0; year < ends.Length; year++)
        {
            fixture.At(Utc($"{2029 + year}-02-20T06:00:00Z"));
            var (quote, paid) = await RenewAsync(package, term, "Year");
            Assert.Equal("2028-02-29T10:00:00", quote.AnchorLocal);
            Assert.Equal(year + 1, quote.Ordinal);
            Assert.Equal(0, quote.GraceDaysIncluded);
            Assert.Equal(Utc(ends[year]), paid.GetProperty("termEndsAt").GetDateTimeOffset());
            term = paid.GetProperty("termId").GetGuid();
        }
        var status = await ShopTermAsync(package, package.Payer);
        Assert.Equal("Active", status.GetProperty("state").GetString());
        Assert.Equal(leap, status.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(Utc("2032-02-29T04:30:00Z"), status.GetProperty("paidThrough").GetDateTimeOffset());
        Assert.Equal(4L, await fixture.CountAsync("subscription_term_facts", "anchor_local = '2028-02-29 10:00:00' AND basis = 'Paid'"));
    }

    internal static async Task<string> ProvenanceAsync(P0308Fixture fixture, CheckoutQuoteV1 quote) =>
        (string)(await fixture.ScalarAsync($"""
            SELECT to_char(payment_effective_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"+00:00"') || '|' ||
                grace_days_included || '|' || renewal_policy_version || '|' || predecessor_term_id
            FROM payment_fulfilments WHERE order_id = '{quote.OrderId:D}'
            """))!;
}
