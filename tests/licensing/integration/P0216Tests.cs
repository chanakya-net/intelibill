using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0216Tests(LicensingPostgreSqlFixture database)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private static readonly DateTimeOffset StartsAt = Utc("2027-01-31T04:30:00Z");
    private static readonly DateTimeOffset EndsAt = Utc("2027-02-28T04:30:00Z");

    [Fact]
    public async Task WithdrawalBeforeEndPreservesOriginalDatesAndPolicy()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var payer = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var shop = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var other = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(payer.Client);
        var term = await SeedPaidAsync(fixture.Hosts, payer, account, shop);
        var otherTerm = await SeedPaidAsync(fixture.Hosts, payer, account, other);
        var paidFacts = await PaidFactsAsync(fixture.Hosts, term);
        var instants = new[] { (EndsAt.AddTicks(-10), "Active"), (EndsAt, "Grace"),
            (EndsAt.AddDays(7).AddTicks(-10), "Grace"), (EndsAt.AddDays(7), "Expired") };
        var original = new List<(string Account, string Shop)>();
        foreach (var (at, _) in instants)
            original.Add(await ReadBothAsync(fixture, payer, shop, account, term, at));
        fixture.Hosts.StatusClock.Now = EndsAt.AddTicks(-10);
        var otherBefore = await P0215Fixture.StatusAsync(other);

        var cancellation = await ScheduleAsync(payer, account, term);
        Assert.Null(TermOf(await AccountStatusAsync(payer, account), term).GetProperty("graceEndsAt").Deserialize<DateTimeOffset?>());

        using var withdrawn = await payer.Client.PostAsJsonAsync(WithdrawalPath(account, term), Withdraw(cancellation, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);

        for (var i = 0; i < instants.Length; i++)
        {
            var (at, state) = instants[i];
            var (accountTerm, shopTerm) = await ReadBothAsync(fixture, payer, shop, account, term, at);
            Assert.Equal(original[i], (accountTerm, shopTerm));
            foreach (var saved in new[] { JsonDocument.Parse(accountTerm).RootElement, JsonDocument.Parse(shopTerm).RootElement })
            {
                Assert.Equal(state, saved.GetProperty("state").GetString());
                Assert.Equal("Paid", saved.GetProperty("basis").GetString());
                Assert.Equal(StartsAt, saved.GetProperty("startsAt").GetDateTimeOffset());
                Assert.Equal(EndsAt, saved.GetProperty("paidThrough").GetDateTimeOffset());
                Assert.Equal(EndsAt.AddDays(7), saved.GetProperty("graceEndsAt").GetDateTimeOffset());
                Assert.False(saved.TryGetProperty("cancellation", out _));
            }
        }

        Assert.Equal(paidFacts, await PaidFactsAsync(fixture.Hosts, term));
        fixture.Hosts.StatusClock.Now = EndsAt.AddTicks(-10);
        Assert.Equal(otherBefore.GetProperty("term").GetRawText(), (await P0215Fixture.StatusAsync(other)).GetProperty("term").GetRawText());
        Assert.Equal(otherTerm, TermOf(await AccountStatusAsync(payer, account), otherTerm).GetProperty("termId").GetGuid());
    }

    private static async Task<(string Account, string Shop)> ReadBothAsync(P0213Fixture fixture, Merchant payer,
        Merchant shop, Guid account, Guid term, DateTimeOffset at)
    {
        fixture.Hosts.StatusClock.Now = at;
        var accountTerm = TermOf(await AccountStatusAsync(payer, account), term).GetRawText();
        var shopStatus = await P0215Fixture.StatusAsync(shop);
        Assert.Equal(shopStatus.GetProperty("term").GetProperty("state").GetString(), shopStatus.GetProperty("state").GetString());
        return (accountTerm, shopStatus.GetProperty("term").GetRawText());
    }

    internal static string WithdrawalPath(Guid account, Guid term) =>
        P0215Tests.CancellationPath(account, term) + "/withdrawal";

    internal static object Withdraw(JsonElement cancellation, Guid operationKey, long? currentVersion = null) => new
    {
        operationKey, currentVersion = currentVersion ?? cancellation.GetProperty("version").GetInt64(),
        changeId = cancellation.GetProperty("changeId").GetGuid(),
        acceptedEffectiveAt = cancellation.GetProperty("effectiveAt").GetDateTimeOffset(), confirmed = true,
    };

    /// <summary>Schedules the term's cancellation through the merchant API and answers its saved summary.</summary>
    internal static async Task<JsonElement> ScheduleAsync(Merchant payer, Guid account, Guid term)
    {
        var path = P0215Tests.CancellationPath(account, term);
        var preview = await P0215Fixture.PreviewAsync(payer, path);
        using var accepted = await payer.Client.PostAsJsonAsync(path, P0215Tests.Accept(preview, Guid.NewGuid()));
        return (await ReadAsync(accepted)).GetProperty("cancellation").Clone();
    }

    internal static async Task<JsonElement> AccountStatusAsync(Merchant payer, Guid account)
    {
        using var response = await payer.Client.GetAsync(new Uri($"/api/billing/accounts/{account:D}/subscriptions", UriKind.Relative));
        return await ReadAsync(response);
    }

    internal static JsonElement TermOf(JsonElement account, Guid term) =>
        account.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("status"))
            .Single(status => status.GetProperty("termId").GetGuid() == term);

    internal static async Task<string> PaidFactsAsync(Hosts hosts, Guid term) =>
        (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(hosts.LicensingOwner, $"""
            SELECT jsonb_build_object('coverage', to_jsonb(t), 'facts', to_jsonb(f))::text
            FROM public.subscription_coverage_terms t JOIN public.subscription_term_facts f USING (account_id, term_id)
            WHERE t.term_id = '{term}'
            """))!;

    internal static async Task<Guid> SeedPaidAsync(Hosts hosts, Merchant payer, Guid account, Merchant shop)
    {
        var term = await SeedTermAsync(hosts, account, new TermSeed(StartsAt, EndsAt, AnchorLocal: "2027-01-31 10:00:00"));
        await AffiliateAsync(hosts, account, payer.UserId, shop.ShopId, shop.UserId, term, StartsAt, EndsAt);
        return term;
    }
}
