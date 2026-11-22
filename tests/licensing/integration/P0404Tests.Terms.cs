using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Globalization;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0404Tests
{
    [Theory]
    [InlineData(0, -1, "Active")]
    [InlineData(0, 0, "Grace")]
    [InlineData(7, -1, "Grace")]
    [InlineData(7, 0, "Expired")]
    public async Task ReleaseProjectsOriginalTermAtFixedBoundaries(int daysPastPaidThrough, int microseconds, string state)
    {
        var boundary = P0202Fixture.WholeSecondNow();
        var paidThrough = boundary.AddDays(-daysPastPaidThrough);
        var at = boundary.AddMicroseconds(microseconds);
        var localEnd = paidThrough.ToOffset(TimeSpan.FromHours(5.5));
        var monthStart = localEnd.AddMonths(-1);
        var month = monthStart.AddMonths(1) == localEnd;
        var startsAt = (month ? monthStart : localEnd.AddYears(-1)).ToUniversalTime();
        await using var checkout = await P0308Fixture.StartAsync(database, startsAt);
        var package = await checkout.PackageAsync(1);
        var fixture = checkout.Checkout.Delivery;
        var clock = fixture.Hosts.StatusClock;
        fixture.Hosts.Fixture.Clock.Now = boundary.AddSeconds(-1);
        var shop = package.Payer;
        var account = package.Account;
        await BackdateFixtureAffiliationAsync(fixture, account, startsAt);
        var (_, termId) = await P0308Fixture.PurchaseAsync(package, month ? "Month" : "Year");
        var termFacts = await TermFactsAsync(fixture, termId);
        using var admin = await SignInSecurityAsync(fixture);
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        var id = await SuspendAsync(admin, path, "Account", null);
        await fixture.DrainAsync(source: true);
        await EnrollAsync(fixture, shop);
        Assert.Equal("Licence.SecuritySuspended", await TradingDecisionAsync(shop));
        var suspended = await fixture.LatestSnapshotAsync(shop.ShopId);
        var releasePath = $"{path}/{id:D}/release";
        clock.Now = boundary.AddSeconds(-1);
        var old = await PreviewReleaseAsync(admin, releasePath);

        clock.Now = at;
        fixture.Hosts.Fixture.Clock.Now = at;
        if (microseconds == 0)
        {
            using var stale = await P0212Fixture.PostAsync(admin, releasePath, old);
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        }
        var preview = await ReadReleasePreviewAsync(admin, releasePath);
        Assert.Equal(state, preview.GetProperty("shops")[0].GetProperty("state").GetString());
        await SubmitReleaseAsync(admin, releasePath);
        var released = await fixture.LatestSnapshotAsync(shop.ShopId);
        Assert.Equal(suspended.Shop!.TermId, released.Shop!.TermId);
        Assert.Equal(suspended.Shop.StartsAt, released.Shop.StartsAt);
        Assert.Equal(suspended.Shop.AccessEndsAt, released.Shop.AccessEndsAt);
        Assert.Equal(suspended.Shop.MemberAllowance, released.Shop.MemberAllowance);
        Assert.Equal(state, released.Shop.State);
        Assert.Equal(suspended.Account!.Revision, released.Account!.ReleasedRevision);
        Assert.Equal(1L, await fixture.Trial.CountAsync("subscription_coverage_terms"));
        Assert.Equal(termFacts, await TermFactsAsync(fixture, termId));
        await fixture.DrainAsync(source: true);
        var status = await AssertTermAsync(shop, state, paidThrough, paidThrough.AddDays(7), released);
        var decision = await TradingDecisionAsync(shop);
        Assert.Equal(state == "Expired" ? "Licence.Expired" : "Allowed", decision);
        await WriteBoundaryEvidenceAsync(state, microseconds, preview, status, decision);
    }

    private static Task<object?> TermFactsAsync(P0213Fixture fixture, Guid term) =>
        fixture.SourceScalarAsync($"""
            SELECT row_to_json(f)::text FROM subscription_term_facts f WHERE term_id = '{term:D}'
            """);

    [Fact]
    public async Task ReleasingAnExpiredTemporaryTermDoesNotInventGrace()
    {
        await using var fixture = await StartAsync();
        var shop = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(shop.Client);
        var clock = fixture.Hosts.StatusClock;
        clock.Now = fixture.Hosts.Fixture.Clock.Now;
        var end = clock.Now.AddSeconds(1);
        var anchor = clock.Now.AddDays(-1).ToOffset(TimeSpan.FromHours(5.5));
        var term = await SeedTermAsync(fixture.Hosts, account,
            new TermSeed(anchor.ToUniversalTime(), end, Basis: "Temporary", PricePaise: null,
                AnchorLocal: FormattableString.Invariant($"{anchor:yyyy-MM-dd HH:mm:ss}")));
        await P0213Fixture.LinkAsync(account, shop, shop, term);
        using var admin = await SignInSecurityAsync(fixture);
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        var id = await SuspendAsync(admin, path, "Account", null);
        await fixture.DrainAsync(source: true);
        await EnrollAsync(fixture, shop);
        clock.Now = end;
        fixture.Hosts.Fixture.Clock.Now = end;
        await SubmitReleaseAsync(admin, $"{path}/{id:D}/release");
        await fixture.DrainAsync(source: true);
        var released = await fixture.LatestSnapshotAsync(shop.ShopId);
        Assert.Equal(end, released.Shop!.AccessEndsAt);
        await AssertTermAsync(shop, "Expired", end, null, released);
        Assert.Equal("Licence.Expired", await TradingDecisionAsync(shop));
        Assert.Equal(1L, await fixture.Trial.CountAsync("subscription_coverage_terms"));
    }

    // The real consent transport stamps the current identity clock. Seed historical affiliation chronology so
    // the simulator can issue an original term whose paid and grace boundaries are close to that clock.
    private static Task BackdateFixtureAffiliationAsync(P0213Fixture fixture, Guid account, DateTimeOffset startsAt)
    {
        var historical = startsAt.AddDays(-1).ToString("O", CultureInfo.InvariantCulture);
        return LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            BEGIN;
            ALTER TABLE shop_affiliations DISABLE TRIGGER retain_rows;
            ALTER TABLE shop_affiliation_requests DISABLE TRIGGER retain_rows;
            UPDATE shop_affiliation_requests SET created_at = timestamptz '{historical}',
                expires_at = timestamptz '{historical}' + interval '7 days' WHERE account_id = '{account:D}';
            UPDATE shop_affiliations SET applied_at = timestamptz '{historical}', accepted_at = timestamptz '{historical}'
                WHERE account_id = '{account:D}';
            ALTER TABLE shop_affiliation_requests ENABLE TRIGGER retain_rows;
            ALTER TABLE shop_affiliations ENABLE TRIGGER retain_rows;
            COMMIT;
            """);
    }

    private static async Task<JsonElement> AssertTermAsync(Merchant shop, string state, DateTimeOffset paidThrough,
        DateTimeOffset? graceEnd, EntitlementSnapshotV1 released)
    {
        using var statusHttp = await ShopStatusAsync(shop.Client, shop.ShopId);
        Assert.True(statusHttp.StatusCode == HttpStatusCode.OK, await statusHttp.Content.ReadAsStringAsync());
        var status = await statusHttp.Content.ReadFromJsonAsync<JsonElement>();
        var term = status.GetProperty("term");
        Assert.Equal(state, status.GetProperty("state").GetString());
        Assert.Equal(paidThrough, term.GetProperty("paidThrough").GetDateTimeOffset());
        Assert.Equal(graceEnd, term.GetProperty("graceEndsAt").Deserialize<DateTimeOffset?>());
        Assert.Equal(released.Shop!.MemberAllowance, term.GetProperty("memberAllowance").GetInt32());
        Assert.False(status.TryGetProperty("enforcedSuspensionMode", out _));
        Assert.False(status.TryGetProperty("suspension", out _));
        return status;
    }

    private static async Task WriteBoundaryEvidenceAsync(string state, int microseconds, JsonElement preview,
        JsonElement merchantStatus, string? decision)
    {
        if (Environment.GetEnvironmentVariable("P0404_EVIDENCE") is not { Length: > 0 } directory) return;
        await File.WriteAllTextAsync(Path.Combine(directory, $"term-{state}-{microseconds}.json"),
            JsonSerializer.Serialize(new { preview, merchantStatus, decision }));
    }
}
