using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0215Tests(LicensingPostgreSqlFixture database)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private static readonly DateTimeOffset StartsAt = Utc("2027-01-31T04:30:00Z");
    private static readonly DateTimeOffset EndsAt = Utc("2027-02-28T04:30:00Z");

    [Fact]
    public async Task ScheduleCancellationPreservesPurchasedAccessUntilExactEnd()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var owner = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var other = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(owner.Client);
        var term = await SeedPaidAsync(fixture.Hosts, owner, account, owner);
        await SeedPaidAsync(fixture.Hosts, owner, account, other);
        var path = CancellationPath(account, term);
        using var previewResponse = await owner.Client.GetAsync(new Uri(path, UriKind.Relative));
        var preview = await ReadAsync(previewResponse);
        Assert.Equal(EndsAt, preview.GetProperty("effectiveAt").GetDateTimeOffset());
        Assert.Equal(owner.ShopId, Assert.Single(preview.GetProperty("affectedShops").EnumerateArray()).GetProperty("shopId").GetGuid());

        using var accepted = await owner.Client.PostAsJsonAsync(path, Accept(preview, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        foreach (var (at, state) in new[] { (EndsAt.AddTicks(-10), "Active"), (EndsAt, "Expired"),
                     (EndsAt.AddTicks(10), "Expired"), (EndsAt.AddDays(1), "Expired") })
        {
            fixture.Hosts.StatusClock.Now = at;
            using var statusResponse = await ShopStatusAsync(owner.Client, owner.ShopId);
            var status = await ReadAsync(statusResponse);
            var saved = status.GetProperty("term");
            Assert.Equal(state, status.GetProperty("state").GetString());
            Assert.Equal("Paid", saved.GetProperty("basis").GetString());
            Assert.Equal(EndsAt, saved.GetProperty("paidThrough").GetDateTimeOffset());
            Assert.Null(saved.GetProperty("graceEndsAt").Deserialize<DateTimeOffset?>());
        }

        using var unrelatedResponse = await ShopStatusAsync(other.Client, other.ShopId);
        Assert.Equal("Grace", (await ReadAsync(unrelatedResponse)).GetProperty("state").GetString());
    }

    internal static string CancellationPath(Guid account, Guid term) =>
        $"/api/billing/accounts/{account:D}/subscriptions/{term:D}/cancellation";

    internal static object Accept(JsonElement preview, Guid operationKey) => new
    {
        operationKey, expectedVersion = preview.GetProperty("version").GetInt32(),
        sourceHash = preview.GetProperty("sourceHash").GetString(),
        acceptedEffectiveAt = preview.GetProperty("effectiveAt").GetDateTimeOffset(),
        affectedShopSetHash = preview.GetProperty("affectedShopSetHash").GetString(),
        affectedShopCount = preview.GetProperty("affectedShopCount").GetInt32(),
        policyVersion = preview.GetProperty("policyVersion").GetString(), confirmed = true,
    };

    private static async Task<Guid> SeedPaidAsync(Hosts hosts, Merchant payer, Guid account, Merchant shop)
    {
        var term = await SeedTermAsync(hosts, account, new TermSeed(StartsAt, EndsAt, AnchorLocal: "2027-01-31 10:00:00"));
        await AffiliateAsync(hosts, account, payer.UserId, shop.ShopId, shop.UserId, term, StartsAt, EndsAt);
        return term;
    }
}
