using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using static Intelibill.Licensing.Integration.Tests.P0212Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0212Tests
{
    [Fact]
    public async Task MerchantSeesRoleCorrectCourtesyAccessWithoutChangingCommerce()
    {
        await using var fixture = await P0212MerchantFixture.StartAsync(database);
        var hosts = fixture.Hosts;
        var (manager, _) = await fixture.Provider.ManagerAsync();
        using var session = manager;
        var payer = await P0209Fixture.SignInAsync(hosts);
        var account = await P0209Fixture.CreateAccountAsync(payer.Client);
        var owner = await P0209Fixture.CreateTradingShopAsync(hosts);
        var staff = await P0209Fixture.SignInAsync(hosts, ShopRole.Staff, owner.ShopId);
        await P0209Fixture.AffiliateAsync(hosts, account, payer.UserId, owner.ShopId, owner.UserId, null, Now.AddDays(-3), null);
        var memberships = await MembershipsAsync();
        await GrantAsync(manager, account, Draft([owner.ShopId], memberAllowance: 6, reason: "Merchant-side courtesy"));

        // Staff: operational facts only, exact end, no account, money, grace or internal reason.
        var shop = await P0209Fixture.ReadAsync(await P0209Fixture.ShopStatusAsync(staff.Client, owner.ShopId));
        var term = shop.GetProperty("term");
        Assert.Equal("Active", shop.GetProperty("state").GetString());
        Assert.Equal("Complimentary", term.GetProperty("basis").GetString());
        Assert.Equal(Ends, term.GetProperty("accessEndsAt").GetDateTimeOffset());
        Assert.Equal(6, term.GetProperty("memberAllowance").GetInt32());
        Assert.Equal(1, term.GetProperty("purchasedCapacity").GetInt32());
        Assert.Equal(JsonValueKind.Null, term.GetProperty("graceEndsAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, shop.GetProperty("accountId").ValueKind);
        Assert.Equal(JsonValueKind.Null, term.GetProperty("commercial").ValueKind);
        Assert.True(shop.GetProperty("observeOnly").GetBoolean());
        Assert.DoesNotContain("Merchant-side courtesy", shop.GetRawText());

        // The billing owner sees the account term; even with commercial authority a grant carries no money.
        var page = await P0209Fixture.ReadAsync(await payer.Client.GetAsync(
            new Uri($"/api/billing/accounts/{account:D}/subscriptions", UriKind.Relative)));
        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal("Complimentary", item.GetProperty("status").GetProperty("basis").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("status").GetProperty("commercial").ValueKind);
        Assert.Equal(owner.ShopId, Assert.Single(item.GetProperty("coveredShops").EnumerateArray()).GetProperty("shopId").GetGuid());
        Assert.DoesNotContain("Merchant-side courtesy", page.GetRawText());
        // Commercial billing membership grants no shop-data access.
        using (var shopData = await payer.Client.GetAsync(new Uri("/api/inventory/batches", UriKind.Relative)))
            Assert.NotEqual(HttpStatusCode.OK, shopData.StatusCode);

        // Exactly at the end: Expired, no grace; observe-only commerce continues and product gates are unchanged.
        hosts.StatusClock.Now = Ends;
        var expired = await P0209Fixture.ReadAsync(await P0209Fixture.ShopStatusAsync(owner.Client, owner.ShopId));
        Assert.Equal("Expired", expired.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, expired.GetProperty("term").GetProperty("graceEndsAt").ValueKind);
        await P0209Tests.PostStockAsync(owner.Client, "Stock at courtesy end");
        using (var refused = await staff.Client.PutAsJsonAsync("/api/expiry-policies", new
        {
            expectedCatalogVersion = 1, reason = "Staff attempt",
            policy = new { mode = "Block", expiryRequired = true, sellThroughExpiryDate = true, warningDays = 7, overrideAllowed = false },
        }))
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(memberships, await MembershipsAsync());
    }

    private async Task<string> MembershipsAsync() =>
        (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase), """
            SELECT jsonb_agg(jsonb_build_array(id, shop_id, user_id, role, status, authority_version) ORDER BY id)::text
            FROM shop_memberships
            """))!;
}
