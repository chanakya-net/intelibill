using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0416Tests
{
    [Fact]
    public async Task ClosedProjectionPreservesBoundedCompletion()
    {
        var clock = new P0201Clock { Now = P0202Fixture.WholeSecondNow() };
        await using var fixture = await P0213Fixture.StartAsync(database, businessClock: clock);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client);
        var local = clock.Now.ToOffset(TimeSpan.FromHours(5.5));
        var anchor = new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, local.Offset);
        var term = await SeedTermAsync(fixture.Hosts, account, new TermSeed(anchor.ToUniversalTime(),
            anchor.AddMonths(1).ToUniversalTime(), AnchorLocal: FormattableString.Invariant($"{anchor:yyyy-MM-dd HH:mm:ss}")));
        await P0213Fixture.LinkAsync(account, owner, owner, term);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        await fixture.MerchantScalarAsync($"""
            UPDATE shop_license_states SET enforcement_activated_at = now() WHERE shop_id = '{owner.ShopId:D}'
            RETURNING shop_id
            """);
        var till = await P0403Tests.OpenTillAsync(fixture, owner);
        var before = await fixture.LatestSnapshotAsync(owner.ShopId);
        var retained = await SeedObservedSubmissionsAsync(fixture, owner, clock.Now);
        var reviewed = await ConfirmShopAsync(owner);
        Assert.True(reviewed.IsSuccessStatusCode, await reviewed.Content.ReadAsStringAsync());
        reviewed.Dispose();

        await fixture.DrainAsync(source: true);
        using var denied = await P0403Tests.WriteTillAsync(owner, "closed-new-till", "New till");
        Assert.Equal("Licence.TradingRestricted", await P0206Fixture.CodeAsync(denied));
        using var history = await owner.Client.GetAsync("/api/sales");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);

        var command = new { countedCash = 50, expectedVersion = till.Item2, operationKey = "close-existing-till" };
        var route = $"/api/tills/sessions/{till.Item1:D}/close";
        using var closed = await owner.Client.PostAsJsonAsync(route, command);
        Assert.True(closed.IsSuccessStatusCode, await closed.Content.ReadAsStringAsync());
        using var duplicate = await owner.Client.PostAsJsonAsync(route, command);
        Assert.Equal(await closed.Content.ReadAsStringAsync(), await duplicate.Content.ReadAsStringAsync());
        using var unsupported = await owner.Client.PostAsJsonAsync($"/api/tills/sessions/{Guid.NewGuid():D}/close",
            command with { operationKey = "other-till" });
        Assert.False(unsupported.IsSuccessStatusCode);

        var after = await fixture.LatestSnapshotAsync(owner.ShopId);
        Assert.Equal(before.Shop!.TermId, after.Shop!.TermId);
        Assert.Equal(before.Shop.AccessEndsAt, after.Shop.AccessEndsAt);
        Assert.Equal(before.Account, after.Account);
        Assert.Equal(retained, await RetainedSubmissionsAsync(fixture, owner));
        Assert.Equal("Closed", after.Shop.State);
        var attemptedRestore = after with { EventId = Guid.NewGuid(), Shop = before.Shop with { Revision = after.Shop.Revision + 1 } };
        var (http, acknowledgement) = await fixture.SendAsync(attemptedRestore);
        http.Dispose();
        Assert.Equal("ConflictingShopScope", acknowledgement?.Disposition);
        await AssertClosureSurvivesRestrictionChangesAsync(fixture, owner, account);
        Assert.Equal(retained, await RetainedSubmissionsAsync(fixture, owner));
    }

    private static async Task AssertClosureSurvivesRestrictionChangesAsync(P0213Fixture fixture, Merchant owner, Guid account)
    {
        var catalogue = fixture.Trial.Catalogue;
        using var platform = await catalogue.SignInOwnerAsync();
        const string email = "closure-security@example.test";
        var seed = await catalogue.EnrollOperatorAsync(platform, email);
        await catalogue.GrantAsync(email, "security-administrator");
        using var security = await catalogue.SignInOperatorAsync(seed, email);
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        var restriction = await P0404Tests.SuspendAsync(security, path, "Shop", owner.ShopId);
        await fixture.DrainAsync(source: true);
        using var blocked = await owner.Client.GetAsync("/api/sales");
        Assert.Equal("Licence.SecuritySuspended", await P0206Fixture.CodeAsync(blocked));

        await P0404Tests.SubmitReleaseAsync(security, $"{path}/{restriction:D}/release");
        await fixture.DrainAsync(source: true);
        await P0403Tests.QueueReconciliationAsync(fixture, owner.ShopId);
        await fixture.DrainAsync(source: true);
        var current = await fixture.LatestSnapshotAsync(owner.ShopId);
        Assert.Equal("Closed", current.Shop?.State);
        Assert.Null(current.Shop?.Suspension);
        using var readable = await owner.Client.GetAsync("/api/sales");
        Assert.Equal(HttpStatusCode.OK, readable.StatusCode);
        using var trade = await P0403Tests.WriteTillAsync(owner, "after-security-release", "Blocked after release");
        Assert.Equal("Licence.TradingRestricted", await P0206Fixture.CodeAsync(trade));
    }

    [Fact]
    public async Task ProviderDetailRequiresMatchingMerchantAcknowledgement()
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client);
        await P0213Fixture.LinkAsync(account, owner, owner);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        using var saved = await ConfirmShopAsync(owner);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());

        var catalogue = fixture.Trial.Catalogue;
        using var admin = await catalogue.SignInOwnerAsync();
        var path = $"/api/admin/accounts/{account:D}/closures";
        using var denied = await admin.SendAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        const string reviewer = "closure-support@example.test";
        var seed = await catalogue.EnrollOperatorAsync(admin, reviewer);
        await catalogue.GrantAsync(reviewer, "support");
        using var support = await catalogue.SignInOperatorAsync(seed, reviewer);
        await AssertProviderAsync(support, path, owner.ShopId, "Pending");
        await fixture.DrainAsync(source: true);
        await AssertProviderAsync(support, path, owner.ShopId, "Pending");
        await fixture.DrainAsync(source: false);
        await AssertProviderAsync(support, path, owner.ShopId, "Applied");
    }

    private static async Task AssertProviderAsync(P0110CookieBrowser support, string path, Guid shop, string state)
    {
        using var response = await support.SendAsync(HttpMethod.Get, path);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
        var closure = Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
        Assert.Equal("NoFinancialChange", closure.GetProperty("financialTreatment").GetString());
        var target = Assert.Single(closure.GetProperty("shops").EnumerateArray());
        Assert.Equal(shop, target.GetProperty("shopId").GetGuid());
        Assert.Equal(state, target.GetProperty("propagation").GetString());
        Assert.False(target.TryGetProperty("workDigest", out _));
        Assert.False(target.TryGetProperty("actorId", out _));
        Assert.False(target.TryGetProperty("deviceId", out _));
    }

    private static async Task<HttpResponseMessage> ConfirmShopAsync(Merchant owner)
    {
        var path = $"/api/licensing/shops/{owner.ShopId:D}";
        var preview = await owner.Client.GetFromJsonAsync<JsonElement>(path + "/closure-preview");
        return await owner.Client.PostAsJsonAsync(path + "/closure", ConfirmShopCommand(preview));
    }

    private static ShopClosureConfirmationV1 ConfirmShopCommand(JsonElement preview) =>
        new(Guid.NewGuid(), preview.GetProperty("closureVersion").GetInt64(), preview.GetProperty("ownerVersion").GetInt64(),
            preview.GetProperty("workDigest").GetString()!, true, true, "NoFinancialChange");
}
