using System.Net;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0417Tests
{
    [Theory]
    [InlineData("Shop", false)]
    [InlineData("Account", false)]
    [InlineData("Trading", false)]
    [InlineData("Shop", true)]
    public async Task SecuritySuspensionSurvivesReopening(string scope, bool race)
    {
        var clock = new P0201Clock { Now = P0202Fixture.WholeSecondNow() };
        await using var fixture = await P0213Fixture.StartAsync(database, businessClock: clock);
        var (owner, account, route) = await CloseAsync(fixture, clock.Now);
        await fixture.DrainAsync(source: true);
        await fixture.MerchantScalarAsync($"""
            UPDATE shop_license_states SET enforcement_activated_at = now() WHERE shop_id = '{owner.ShopId:D}'
            RETURNING shop_id
            """);
        using var manager = await SignInAsync(fixture);
        using var security = await SignInAsync(fixture, "security-administrator");
        var before = Command(await PreviewAsync(manager, route), owner);
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        var racingReopening = race ? P0212Fixture.PostAsync(manager, route, before) : null;
        var restriction = scope == "Trading"
            ? await SuspendTradingAsync(security, path, owner.ShopId)
            : await P0404Tests.SuspendAsync(security, path, scope, scope == "Shop" ? owner.ShopId : null);
        using var attempted = await (racingReopening ?? P0212Fixture.PostAsync(manager, route, before));
        var mode = scope == "Trading" ? "Trading" : "Security";
        if (!race || attempted.StatusCode != HttpStatusCode.Created)
        {
            Assert.Equal("Reopening.VersionConflict", await P0206Fixture.CodeAsync(attempted));
            var impact = await PreviewAsync(manager, route);
            Assert.Equal(mode, impact.GetProperty("survivingSuspension").GetProperty("mode").GetString());
            using var opened = await P0212Fixture.PostAsync(manager, route, Command(impact, owner));
            Assert.True(opened.StatusCode == HttpStatusCode.Created, await opened.Content.ReadAsStringAsync());
        }
        var state = await fixture.LatestSnapshotAsync(owner.ShopId);
        Assert.Equal(mode, scope == "Account" ? state.Account?.Restriction : state.Shop?.Suspension?.Mode);
        Assert.Equal("Active", state.Shop?.State);
        Assert.Equal(0L, await fixture.Trial.CountAsync("operational_restriction_releases"));
        await fixture.DrainAsync(source: true);
        if (mode == "Security")
        {
            using var blocked = await owner.Client.GetAsync("/api/sales");
            Assert.Equal("Licence.SecuritySuspended", await P0206Fixture.CodeAsync(blocked));
        }
        else
        {
            using var blocked = await P0403Tests.WriteTillAsync(owner, "trading-restricted", "Trading restriction");
            Assert.Equal("Licence.TradingRestricted", await P0206Fixture.CodeAsync(blocked));
        }
        await P0404Tests.SubmitReleaseAsync(security, $"{path}/{restriction:D}/release");
        await fixture.DrainAsync(source: true);
        using var readable = await owner.Client.GetAsync("/api/sales");
        Assert.Equal(HttpStatusCode.OK, readable.StatusCode);
    }

    private static async Task<Guid> SuspendTradingAsync(P0110CookieBrowser admin, string path, Guid shop)
    {
        using var preview = await P0212Fixture.PostAsync(admin, path + "/preview", new { scope = "Shop", shopId = shop });
        Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        using var impact = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
        using var applied = await P0212Fixture.PostAsync(admin, path, new
        {
            operationKey = Guid.NewGuid(), scope = "Shop", shopId = shop, mode = "Trading",
            customerExplanation = "Trading restricted", internalReason = "Review required", reviewReference = "TRADING-898",
            expectedVersion = impact.RootElement.GetProperty("version").GetInt64(),
            impactDigest = impact.RootElement.GetProperty("impactDigest").GetString(),
        });
        Assert.True(applied.StatusCode == HttpStatusCode.Created, await applied.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await applied.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }
}
