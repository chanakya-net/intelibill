using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0605Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    internal static string PathFor(Package p, Guid term) =>
        $"/api/billing/accounts/{p.Account}/subscriptions/{term}/coverage-replacements";

    internal static async Task<(Package Package, Guid Term)> PrepareAsync(P0308Fixture fixture)
    {
        var package = await fixture.PackageAsync(4, ["Central", "East", "North", "South"]);
        var (_, term) = await PurchaseAsync(package with { Shops = package.Shops[..3] }, "Month");
        await fixture.Checkout.Delivery.DrainAsync(true);
        await fixture.Checkout.Delivery.DrainAsync(false);
        fixture.At(February14);
        return (package, term);
    }

    [Fact]
    public async Task PreviewNamesCoveredShopsRemainingPermissionAndUncoveredCandidates()
    {
        await using var fixture = await StartAsync(database, January31);
        var (package, term) = await PrepareAsync(fixture);
        using var preview = await package.Payer.Client.GetAsync(PathFor(package, term));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var body = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(February29, body.GetProperty("effectiveAt").GetDateTimeOffset());
        var north = Assert.Single(body.GetProperty("coveredShops").EnumerateArray(),
            x => x.GetProperty("shopId").GetGuid() == package.Shops[2].ShopId);
        Assert.Equal("North", north.GetProperty("shopName").GetString());
        Assert.Equal(JsonValueKind.Null, north.GetProperty("permitDeadline").ValueKind);
        var incoming = Assert.Single(body.GetProperty("eligibleShops").EnumerateArray());
        Assert.Equal("South", incoming.GetProperty("shopName").GetString());
        Assert.Equal(package.Shops[3].ShopId, incoming.GetProperty("shopId").GetGuid());
    }
}
