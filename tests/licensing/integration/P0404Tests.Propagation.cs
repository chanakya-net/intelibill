using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0404Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentReleaseOnlyClearsItsSelectedScope(bool accountFirst)
    {
        await using var fixture = await StartAsync();
        var shop = await CreateTradingShopAsync(fixture.Hosts);
        var sibling = await CreateTradingShopAsync(fixture.Hosts);
        var account = await LinkAsync(fixture, shop);
        await LinkAsync(fixture, sibling, existingAccount: account, payer: shop);
        using var admin = await SignInSecurityAsync(fixture);
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        var accountId = await SuspendAsync(admin, path, "Account", null);
        var shopId = await SuspendAsync(admin, path, "Shop", shop.ShopId);
        var firstId = accountFirst ? accountId : shopId;
        var secondId = accountFirst ? shopId : accountId;
        var releasePath = $"{path}/{firstId:D}/release";
        var impact = await ReadReleasePreviewAsync(admin, releasePath);
        var selectedImpact = impact.GetProperty("shops").EnumerateArray()
            .Single(item => item.GetProperty("shopId").GetGuid() == shop.ShopId);
        Assert.Equal("Security", selectedImpact.GetProperty("survivingSuspension").GetProperty("mode").GetString());
        var command = Command(impact);
        using var retryBrowser = admin.Fork(fixture.Trial.Catalogue.Api.CreateClient());
        await RaceReleasesAsync(admin, retryBrowser, releasePath, command, command, HttpStatusCode.OK);
        var snapshot = await fixture.LatestSnapshotAsync(shop.ShopId);
        var siblingSnapshot = await fixture.LatestSnapshotAsync(sibling.ShopId);
        Assert.Equal(accountFirst ? "Unrestricted" : "Security", snapshot.Account!.Restriction);
        Assert.Equal(snapshot.Account, siblingSnapshot.Account);
        Assert.Equal(accountFirst ? "Security" : null, snapshot.Shop!.Suspension?.Mode);
        Assert.Null(siblingSnapshot.Shop!.Suspension);
        Assert.Equal(1L, await fixture.Trial.CountAsync("operational_restriction_releases"));
        var replacement = await SuspendAsync(admin, path, accountFirst ? "Account" : "Shop", accountFirst ? null : shop.ShopId);
        Assert.NotEqual(firstId, replacement);
        using var replay = await P0212Fixture.PostAsync(admin, releasePath, command);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var stale = await P0212Fixture.PostAsync(admin, releasePath, command with { OperationKey = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var secondPath = $"{path}/{secondId:D}/release";
        var second = await PreviewReleaseAsync(admin, secondPath);
        await RaceReleasesAsync(admin, retryBrowser, secondPath, second, second with { OperationKey = Guid.NewGuid() },
            HttpStatusCode.Conflict);
        var remaining = await fixture.LatestSnapshotAsync(shop.ShopId);
        Assert.Equal(accountFirst ? "Security" : null, remaining.Account!.Suspension?.Mode);
        Assert.Equal(accountFirst ? null : "Security", remaining.Shop!.Suspension?.Mode);
    }

    private static async Task RaceReleasesAsync(P0110CookieBrowser admin, P0110CookieBrowser other, string path,
        ReleaseRequest first, ReleaseRequest second, HttpStatusCode replayStatus)
    {
        var results = await Task.WhenAll(P0212Fixture.PostAsync(admin, path, first), P0212Fixture.PostAsync(other, path, second));
        try
        {
            Assert.Single(results, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Single(results, response => response.StatusCode == replayStatus);
        }
        finally
        {
            foreach (var response in results) response.Dispose();
        }
    }

    [Theory]
    [InlineData("Account")]
    [InlineData("Shop")]
    public async Task ReorderedScopedReleaseConvergesWithoutDroppingSiblingRestriction(string scope)
    {
        await using var fixture = await StartAsync();
        var shop = await CreateTradingShopAsync(fixture.Hosts);
        var account = await LinkAsync(fixture, shop);
        using var admin = await SignInSecurityAsync(fixture);
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        await SuspendAsync(admin, path, scope == "Shop" ? "Account" : "Shop", scope == "Shop" ? null : shop.ShopId);
        var selected = scope == "Shop" ? shop.ShopId : (Guid?)null;
        var id = await SuspendAsync(admin, path, scope, selected);
        var before = await fixture.LatestSnapshotAsync(shop.ShopId);
        Assert.Equal("Applied", await DispositionAsync(fixture, before));
        await SubmitReleaseAsync(admin, $"{path}/{id:D}/release");
        var released = await fixture.LatestSnapshotAsync(shop.ShopId);
        var markerless = released with { EventId = Guid.NewGuid(),
            Account = released.Account! with { ReleasedRevision = null },
            Shop = released.Shop! with { ReleasedRevision = null } };
        Assert.Equal($"Conflicting{scope}Scope", await DispositionAsync(fixture, markerless));
        fixture.Hosts.StatusClock.Now = fixture.Hosts.StatusClock.Now.AddMicroseconds(1);
        await SuspendAsync(admin, path, scope, selected);
        var replacement = await fixture.LatestSnapshotAsync(shop.ShopId);
        Assert.Equal("Applied", await DispositionAsync(fixture, replacement));
        Assert.Equal("Superseded", await DispositionAsync(fixture, released));
        var regressed = replacement with { EventId = Guid.NewGuid(),
            Account = replacement.Account! with { Revision = replacement.Account.Revision + 1, PreviousReleaseRevision = null },
            Shop = replacement.Shop! with { Revision = replacement.Shop.Revision + 1, ReleasedRevision = null } };
        Assert.Equal($"Conflicting{scope}Scope", await DispositionAsync(fixture, regressed));
        Assert.Equal("Security", await fixture.MerchantScalarAsync(
            $"SELECT restriction FROM account_license_restrictions WHERE account_id = '{account:D}'"));
        Assert.Equal("Security", await fixture.MerchantScalarAsync(
            $"SELECT suspension_mode FROM shop_license_states WHERE shop_id = '{shop.ShopId:D}'"));
    }

    [Fact]
    public async Task ReleaseAcknowledgementRequiresTheCorrespondingSignedRevisions()
    {
        await using var fixture = await StartAsync();
        var shop = await CreateTradingShopAsync(fixture.Hosts);
        var sibling = await CreateTradingShopAsync(fixture.Hosts);
        var account = await LinkAsync(fixture, shop);
        await LinkAsync(fixture, sibling, existingAccount: account, payer: shop);
        using var admin = await SignInSecurityAsync(fixture);
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        var id = await SuspendAsync(admin, path, "Account", null);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        await EnrollAsync(fixture, shop);
        var pending = await SubmitReleaseAsync(admin, $"{path}/{id:D}/release");
        Assert.Equal("Acknowledged", pending.GetProperty("propagation").GetString());
        Assert.Equal("Pending", pending.GetProperty("release").GetProperty("propagation").GetString());
        Assert.Equal("Licence.SecuritySuspended", await TradingDecisionAsync(shop));
        await fixture.DrainAsync(source: true);
        Assert.Equal("Pending", await ReleasePropagationAsync(admin, path, id));
        Assert.Equal("Allowed", await TradingDecisionAsync(shop));
        Assert.True(await fixture.DeliverAsync(source: false));
        Assert.Equal("Pending", await ReleasePropagationAsync(admin, path, id));
        await fixture.DrainAsync(source: false);
        Assert.Equal("Acknowledged", await ReleasePropagationAsync(admin, path, id));

        var late = await CreateTradingShopAsync(fixture.Hosts);
        await LinkAsync(fixture, late, existingAccount: account, payer: shop);
        Assert.Equal("Pending", await ReleasePropagationAsync(admin, path, id));
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        Assert.Equal("Acknowledged", await ReleasePropagationAsync(admin, path, id));
    }

    private static async Task<string?> ReleasePropagationAsync(P0110CookieBrowser admin, string path, Guid id)
    {
        using var response = await admin.SendAsync(HttpMethod.Get, $"{path}/{id:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("release").GetProperty("propagation").GetString();
    }

    private static async Task<string?> DispositionAsync(P0213Fixture fixture, EntitlementSnapshotV1 snapshot)
    {
        var (response, acknowledgement) = await fixture.SendAsync(snapshot with { EventId = Guid.NewGuid() });
        using (response) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return acknowledgement?.Disposition;
    }

    private static async Task<JsonElement> SubmitReleaseAsync(P0110CookieBrowser admin, string path)
    {
        using var response = await P0212Fixture.PostAsync(admin, path, await PreviewReleaseAsync(admin, path));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private Task<P0213Fixture> StartAsync() => P0213Fixture.StartAsync(database,
        businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });

    private static async Task EnrollAsync(P0213Fixture fixture, Merchant shop) =>
        Assert.Equal(shop.ShopId, await fixture.MerchantScalarAsync($"""
            UPDATE shop_license_states SET enforcement_activated_at = now()
            WHERE shop_id = '{shop.ShopId:D}' RETURNING shop_id
            """));

    private static async Task<string?> TradingDecisionAsync(Merchant shop)
    {
        using var response = await shop.Client.GetAsync($"/api/licensing/shops/{shop.ShopId:D}/trading-authority");
        return response.IsSuccessStatusCode ? "Allowed" : await P0206Fixture.CodeAsync(response);
    }
}
