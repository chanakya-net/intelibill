using System.Globalization;
using System.Net;
using System.Text.Json;
using Intelibill.Licensing.Domain.Policies;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0209Tests
{
    [Fact]
    public async Task ProviderAndMerchantShareBasisWithLiveCapabilityRedaction()
    {
        await using var fixture = await P0206Fixture.StartAsync(database);
        var anchor = new BillingAnchor(BillingCalendar.LocalStart(fixture.Clock.GetUtcNow().AddDays(-10)), BillingCalendar.Zone, BillingPeriod.Month);
        var (starts, ends) = BillingCalendar.Period(anchor, 0);
        var (account, payer) = await SeedAccountAsync(fixture.Superuser);
        var term = await SeedTermAsync(fixture.Superuser, account, new TermSeed(starts, ends,
            AnchorLocal: anchor.LocalStart.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
        var shop = Guid.NewGuid();
        await AffiliateAsync(fixture.Superuser, account, payer, shop, Guid.NewGuid(), term, starts, ends);
        var accountPath = $"/api/admin/accounts/{account:D}/status";
        var shopPath = $"/api/admin/accounts/{account:D}/shops/{shop:D}/status";

        string seed;
        using (var owner = await fixture.SignInOwnerAsync())
        {
            // Platform Owner alone holds no status capability.
            using var denied = await owner.SendAsync(HttpMethod.Get, accountPath);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal("Status.AccessDenied", await P0206Fixture.CodeAsync(denied));
            seed = await fixture.EnrollOperatorAsync(owner);
        }

        await fixture.GrantAsync(P0114Tests.Invitee, "support");
        using var support = await fixture.SignInOperatorAsync(seed);
        using var supportAccount = await support.SendAsync(HttpMethod.Get, accountPath);
        var item = Assert.Single((await P0206Fixture.BodyAsync(supportAccount)).GetProperty("items").EnumerateArray());
        var status = item.GetProperty("status");
        Assert.Equal("Active", status.GetProperty("state").GetString());
        Assert.Equal(ends, status.GetProperty("paidThrough").GetDateTimeOffset());
        Assert.Equal(ends.AddDays(7), status.GetProperty("graceEndsAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("commercial").ValueKind);
        Assert.Equal(shop, Assert.Single(item.GetProperty("coveredShops").EnumerateArray()).GetProperty("shopId").GetGuid());
        using var supportShop = await support.SendAsync(HttpMethod.Get, shopPath);
        var shopStatus = await P0206Fixture.BodyAsync(supportShop);
        Assert.Equal(JsonValueKind.Null, shopStatus.GetProperty("accountId").ValueKind);
        Assert.Equal(status.GetProperty("graceEndsAt").GetDateTimeOffset(), shopStatus.GetProperty("term").GetProperty("graceEndsAt").GetDateTimeOffset());
        Assert.Equal("NotAvailable", shopStatus.GetProperty("entitlementSynchronization").GetString());
        using var wrongAccount = await support.SendAsync(HttpMethod.Get, $"/api/admin/accounts/{Guid.NewGuid():D}/shops/{shop:D}/status");
        Assert.Equal(HttpStatusCode.NotFound, wrongAccount.StatusCode);

        await fixture.GrantAsync(P0114Tests.Invitee, "billing");
        using var stale = await support.SendAsync(HttpMethod.Get, accountPath);
        Assert.NotEqual(HttpStatusCode.OK, stale.StatusCode);
        using var billing = await fixture.SignInOperatorAsync(seed);
        using var billingShop = await billing.SendAsync(HttpMethod.Get, shopPath);
        var commercial = await P0206Fixture.BodyAsync(billingShop);
        Assert.Equal(account, commercial.GetProperty("accountId").GetGuid());
        Assert.Equal(149900, commercial.GetProperty("term").GetProperty("commercial").GetProperty("pricePaise").GetInt64());

        await fixture.GrantAsync(P0114Tests.Invitee, "security-administrator");
        using var security = await fixture.SignInOperatorAsync(seed);
        using var revoked = await security.SendAsync(HttpMethod.Get, shopPath);
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        Assert.DoesNotContain("149900", await revoked.Content.ReadAsStringAsync());
    }
}
