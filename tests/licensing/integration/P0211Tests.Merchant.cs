using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0211Tests
{
    [Fact]
    public async Task MerchantReadsTheExactExtendedDeadlineWithNoGraceOrCommerceChange()
    {
        await using var fixture = await P0210Fixture.StartAsync(database);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var staff = await SignInAsync(fixture.Hosts, ShopRole.Staff, owner.ShopId);
        var payer = await SignInAsync(fixture.Hosts);
        var account = await CreateAccountAsync(payer.Client);
        await fixture.LinkAsync(account, payer.UserId, owner);
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(owner, fixture.Accept())).Status);

        var authority = await MembershipsAsync(database);
        var originalEnd = P0210Fixture.Start.AddDays(14);
        fixture.Hosts.StatusClock.Now = originalEnd;
        Assert.Equal("Expired", (await MerchantStatusAsync(owner)).GetProperty("state").GetString());
        await ApproveAsync(fixture, account, owner.ShopId);

        AssertTerm(await MerchantStatusAsync(owner), "Active");
        using var accountResponse = await payer.Client.GetAsync(new Uri($"/api/billing/accounts/{account:D}/subscriptions", UriKind.Relative));
        var page = await ReadAsync(accountResponse);
        AssertTerm(Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("status"), "Active");

        fixture.Hosts.StatusClock.Now = End.AddTicks(-10);
        AssertTerm(await MerchantStatusAsync(staff with { ShopId = owner.ShopId }), "Active");
        fixture.Hosts.StatusClock.Now = End;
        AssertTerm(await MerchantStatusAsync(owner), "Expired");
        await P0209Tests.PostStockAsync(owner.Client, "Stock at extended trial end");
        Assert.Equal(authority, await MembershipsAsync(database));
    }

    private static async Task<JsonElement> MerchantStatusAsync(Merchant merchant)
    {
        using var response = await ShopStatusAsync(merchant.Client, merchant.ShopId);
        var status = await ReadAsync(response);
        Assert.True(status.GetProperty("observeOnly").GetBoolean());
        return status;
    }

    private static void AssertTerm(JsonElement status, string state)
    {
        Assert.Equal(state, status.GetProperty("state").GetString());
        var term = status.TryGetProperty("term", out var nested) ? nested : status;
        Assert.Equal("Trial", term.GetProperty("basis").GetString());
        Assert.Equal("Independent", term.GetProperty("coverageMode").GetString());
        Assert.Equal(1, term.GetProperty("purchasedCapacity").GetInt32());
        Assert.Equal(5, term.GetProperty("memberAllowance").GetInt32());
        Assert.Equal(End, term.GetProperty("accessEndsAt").GetDateTimeOffset());
        Assert.Equal(End, term.GetProperty("paidThrough").GetDateTimeOffset());
        if (state == "Active") Assert.Equal(End, term.GetProperty("nextDeadline").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, term.GetProperty("graceEndsAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, term.GetProperty("commercial").ValueKind);
        Assert.DoesNotContain(Review, status.GetRawText());
        Assert.DoesNotContain(Reason, status.GetRawText());
    }

    internal static async Task<string> MembershipsAsync(LicensingPostgreSqlFixture database) =>
        (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase), """
            SELECT jsonb_agg(jsonb_build_array(id, shop_id, user_id, role, status, authority_version) ORDER BY id)::text
            FROM shop_memberships
            """))!;

    private static async Task<JsonElement> ApproveAsync(P0210Fixture fixture, Guid account, Guid shop, DateTimeOffset? end = null)
    {
        var path = Path(account, shop);
        var preview = await PreviewAsync(fixture.CatalogueManager!, path, end);
        using var response = await P0212Fixture.PostAsync(fixture.CatalogueManager!, path, Confirm(preview, Guid.NewGuid()));
        Assert.True(response.StatusCode == System.Net.HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await P0206Fixture.BodyAsync(response);
    }
}
