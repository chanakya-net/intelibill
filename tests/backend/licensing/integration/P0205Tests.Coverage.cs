using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0205Tests
{
    [Fact]
    public async Task ConcurrentAccounts_OnlyOneEffectiveAffiliationAndPayerGainsNoShopAccess()
    {
        await using var host = await StartAsync();
        using var payer = host.Merchant.CreateClient();
        using var otherPayer = host.Merchant.CreateClient();
        using var owner = host.Merchant.CreateClient();
        await SignInAsync(host.Merchant, payer);
        await SignInAsync(host.Merchant, otherPayer);
        var shop = await SignInAsync(host.Merchant, owner, withShop: true);
        var before = await payer.GetStringAsync("/api/shops/me");
        var account = await CreateAccountAsync(payer);
        var otherAccount = await CreateAccountAsync(otherPayer);
        var first = ConsentPath(account, shop, await RequestAsync(payer, account, shop));
        var second = ConsentPath(otherAccount, shop, await RequestAsync(otherPayer, otherAccount, shop));
        await DecideAsync(owner, first, "grant");
        await DecideAsync(owner, second, "grant");

        var operations = await Task.WhenAll(AcceptAsync(owner, first), AcceptAsync(owner, second));
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, operations.Order());
        await WaitForAffiliationAsync(operations[0] == HttpStatusCode.OK ? payer : otherPayer,
            operations[0] == HttpStatusCode.OK ? account : otherAccount, "Applied");
        Assert.Equal(1L, await P0202Fixture.CountAsync(database, host.Fixture, "SELECT count(*) FROM public.shop_affiliations"));
        Assert.Equal(before, await payer.GetStringAsync("/api/shops/me"));
        using var denied = await payer.GetAsync($"/api/shops/{shop:D}");
        Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
        using var switched = await payer.PostAsJsonAsync("/api/shops/switch", new { shopId = shop });
        Assert.NotEqual(HttpStatusCode.OK, switched.StatusCode);
        using var items = await payer.GetAsync("/api/items");
        Assert.NotEqual(HttpStatusCode.OK, items.StatusCode);
    }

    [Fact]
    public async Task IndependentSubscriptionsKeepTheirOwnDates()
    {
        await using var host = await StartAsync();
        using var payer = host.Merchant.CreateClient();
        using var owner = host.Merchant.CreateClient();
        await SignInAsync(host.Merchant, payer);
        var shop = await SignInAsync(host.Merchant, owner, withShop: true);
        var account = await CreateAccountAsync(payer);
        var now = host.Fixture.Clock.Now;
        var first = await SeedTermAsync(database.Bootstrap(host.Fixture.Owner.Database), account, now.AddDays(-5), now.AddDays(25));
        var second = await SeedTermAsync(database.Bootstrap(host.Fixture.Owner.Database), account, now.AddDays(-10), now.AddDays(45));
        var request = await RequestAsync(payer, account, shop, first);
        var consent = ConsentPath(account, shop, request);
        await DecideAsync(owner, consent, "grant");
        await DecideAsync(owner, consent, "accept");
        await WaitForAffiliationAsync(payer, account, "Applied");

        var view = await payer.GetFromJsonAsync<JsonElement>($"/api/billing/accounts/{account:D}/affiliations");
        var dates = view.GetProperty("terms").EnumerateArray().ToDictionary(
            item => item.GetProperty("termId").GetGuid(), item => item.GetProperty("endsAt").GetDateTimeOffset());
        Assert.Equal(now.AddDays(25), dates[first]);
        Assert.Equal(now.AddDays(45), dates[second]);
        Assert.Equal(first, Assert.Single(view.GetProperty("requests").EnumerateArray()).GetProperty("term").GetProperty("termId").GetGuid());

        var context = database.Bootstrap(host.Fixture.Owner.Database);
        Assert.Equal(now.AddDays(25).UtcDateTime, await LicensingPostgreSqlFixture.ReadScalarAsync(context,
            "SELECT coverage_ends_at FROM public.shop_affiliations"));
        await using var runtime = new NpgsqlConnection(host.Fixture.Runtime.ConnectionString);
        await runtime.OpenAsync();
        await using var update = new NpgsqlCommand("TRUNCATE TABLE public.shop_affiliations", runtime);
        Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync())).SqlState);
    }

    [Fact]
    public async Task GroupedAssignmentsRaceForPurchasedSlotsAndKeepTheExistingEnd()
    {
        await using var host = await StartAsync();
        using var payer = host.Merchant.CreateClient();
        using var first = host.Merchant.CreateClient();
        using var second = host.Merchant.CreateClient();
        using var third = host.Merchant.CreateClient();
        await SignInAsync(host.Merchant, payer);
        var account = await CreateAccountAsync(payer);
        var now = host.Fixture.Clock.Now;
        var term = await SeedTermAsync(database.Bootstrap(host.Fixture.Owner.Database), account,
            now.AddDays(-5), now.AddDays(25), grouped: true);

        var owners = new[] { first, second, third };
        var links = new List<string>();
        foreach (var owner in owners)
        {
            var shop = await SignInAsync(host.Merchant, owner, withShop: true);
            var link = ConsentPath(account, shop, await RequestAsync(payer, account, shop, term));
            await DecideAsync(owner, link, "grant");
            links.Add(link);
        }

        Assert.All(await Task.WhenAll(owners.Zip(links, AcceptAsync)), status => Assert.Equal(HttpStatusCode.OK, status));
        await WaitForAffiliationAsync(payer, account, "NeedsReview");
        var view = await payer.GetFromJsonAsync<JsonElement>($"/api/billing/accounts/{account:D}/affiliations");
        Assert.Equal(2, view.GetProperty("requests").EnumerateArray().Count(row => row.GetProperty("status").GetString() == "Applied"));
        Assert.Equal(now.AddDays(25), Assert.Single(view.GetProperty("terms").EnumerateArray()).GetProperty("endsAt").GetDateTimeOffset());
        Assert.Equal(2L, await P0202Fixture.CountAsync(database, host.Fixture,
            "SELECT count(*) FROM public.shop_affiliations a JOIN public.subscription_coverage_terms t ON a.term_id = t.term_id WHERE a.coverage_ends_at = t.ends_at"));
    }

    [Fact]
    public async Task AnAcceptedSelectionAtItsExactTermEndDoesNotCreateEffectiveCoverage()
    {
        await using var host = await StartAsync();
        using var payer = host.Merchant.CreateClient();
        using var owner = host.Merchant.CreateClient();
        await SignInAsync(host.Merchant, payer);
        var shop = await SignInAsync(host.Merchant, owner, withShop: true);
        var account = await CreateAccountAsync(payer);
        var starts = host.Fixture.Clock.Now;
        var ends = starts.AddMinutes(1);
        var term = await SeedTermAsync(database.Bootstrap(host.Fixture.Owner.Database), account, starts, ends);
        var path = ConsentPath(account, shop, await RequestAsync(payer, account, shop, term));
        await DecideAsync(owner, path, "grant");
        host.Fixture.Clock.Now = ends;
        using var expired = await payer.PostAsJsonAsync($"/api/billing/accounts/{account:D}/affiliations", new
        {
            operationKey = Guid.NewGuid(), shopId = shop, termId = term, withoutCoverage = false,
        });
        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
        await DecideAsync(owner, path, "accept");
        await WaitForAffiliationAsync(payer, account, "NeedsReview");
        Assert.Equal(0L, await P0202Fixture.CountAsync(database, host.Fixture, "SELECT count(*) FROM public.shop_affiliations"));
    }

    private static async Task<HttpStatusCode> AcceptAsync(HttpClient owner, string path)
    {
        using var response = await owner.PostAsJsonAsync(path + "/accept", new { operationKey = Guid.NewGuid() });
        return response.StatusCode;
    }

    internal static async Task<Guid> SeedTermAsync(NpgsqlConnectionStringBuilder settings, Guid account,
        DateTimeOffset starts, DateTimeOffset ends, bool grouped = false)
    {
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        await using var sql = new NpgsqlCommand("""
            INSERT INTO public.subscription_coverage_terms (term_id, account_id, mode, capacity, starts_at, ends_at)
            VALUES ($1, $2, $3, $4, $5, $6)
            """, connection);
        var term = Guid.NewGuid();
        sql.Parameters.AddWithValue(term);
        sql.Parameters.AddWithValue(account);
        sql.Parameters.AddWithValue(grouped ? "Grouped" : "Independent");
        sql.Parameters.AddWithValue(grouped ? 2 : 1);
        sql.Parameters.AddWithValue(starts);
        sql.Parameters.AddWithValue(ends);
        await sql.ExecuteNonQueryAsync();
        return term;
    }
}
