using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

internal static class P0215Fixture
{
    // A fixture subscription that covers today's consent date. Access evaluation is then pinned to
    // exact instants within the resulting billing year, independently of auth and delivery clocks.
    internal static async Task<(Guid Term, DateTimeOffset End)> SeedCurrentTermAsync(P0213Fixture fixture,
        Guid account, int capacity = 1)
    {
        var now = fixture.AuthClock.GetUtcNow();
        var year = now.ToOffset(TimeSpan.FromMinutes(330)).Year;
        var starts = new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.FromMinutes(330));
        var ends = starts.AddYears(1).ToUniversalTime();
        var anchor = starts.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var term = await SeedTermAsync(fixture.Hosts, account, new(starts.ToUniversalTime(), ends, Period: "Year",
            AnchorLocal: anchor, Grouped: capacity > 1, Capacity: capacity));
        fixture.Hosts.StatusClock.Now = now.AddMinutes(1);
        return (term, ends);
    }

    internal static async Task<string> RequestAsync(Merchant payer, Merchant shop, Guid account, Guid term)
    {
        using var response = await payer.Client.PostAsJsonAsync($"/api/billing/accounts/{account:D}/affiliations", new
        {
            operationKey = Guid.NewGuid(), shopId = shop.ShopId, termId = term, withoutCoverage = false,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestId").GetGuid();
        return $"/api/billing/accounts/{account:D}/affiliations/{shop.ShopId:D}/{id:D}/consent";
    }

    internal static async Task ApplyRequestAsync(Merchant shop, string consent)
    {
        Assert.Equal("Granted", await P0205Tests.DecideAsync(shop.Client, consent, "grant"));
        Assert.Equal("Accepted", await P0205Tests.DecideAsync(shop.Client, consent, "accept"));
        await P0205Tests.WaitForConsentAsync(shop.Client, consent, "Applied");
    }

    internal static async Task<JsonElement> PreviewAsync(Merchant payer, string path)
    {
        using var response = await payer.Client.GetAsync(new Uri(path, UriKind.Relative));
        Assert.True(response.Headers.CacheControl?.NoStore);
        return await ReadAsync(response);
    }

    internal static async Task<JsonElement> StatusAsync(Merchant merchant)
    {
        using var response = await ShopStatusAsync(merchant.Client, merchant.ShopId);
        return await ReadAsync(response);
    }
}
