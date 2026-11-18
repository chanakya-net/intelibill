using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0301Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task MerchantCreatesFrozenFullPaymentQuoteForEveryNamedShop()
    {
        await using var fixture = await StartCheckoutAsync(database, new P0201Clock { Now = P0210Fixture.Start });
        var payer = await SignInAsync(fixture.Hosts);
        var account = await CreateAccountAsync(payer.Client);
        var shops = new List<Guid>();
        for (var index = 0; index < 4; index++)
        {
            var shop = await SignInAsync(fixture.Hosts, ShopRole.Owner);
            await fixture.LinkAsync(account, payer.UserId, shop);
            shops.Add(shop.ShopId);
        }
        var version = await PublishPlanAsync(fixture);

        var path = $"/api/billing/accounts/{account:D}/orders";
        using var response = await payer.Client.PostAsJsonAsync(path, new
        {
            operationKey = Guid.NewGuid(), planVersionId = version, period = "Month", shopIds = shops,
            extraShops = 1, expectedRevision = 1,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var quote = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(27000, quote.GetProperty("amountPaise").GetInt64());
        Assert.Equal("INR", quote.GetProperty("currency").GetString());
        Assert.Equal(4, quote.GetProperty("capacity").GetInt32());
        Assert.Equal(4, quote.GetProperty("shopCount").GetInt32());
        Assert.Equal(TimeSpan.FromMinutes(15), quote.GetProperty("expiresAt").GetDateTimeOffset() -
            quote.GetProperty("createdAt").GetDateTimeOffset());
        var order = quote.GetProperty("orderId").GetGuid();
        var page = await payer.Client.GetFromJsonAsync<JsonElement>($"{path}/{order:D}/shops");
        Assert.Equal(shops.Order(), page.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("shopId").GetGuid()).Order());
        Assert.All(page.GetProperty("items").EnumerateArray(), row => Assert.Equal("Status Fixture Shop", row.GetProperty("name").GetString()));
        Assert.Equal(0L, await fixture.CountAsync("subscription_coverage_terms"));
    }

    internal static async Task<Guid> PublishPlanAsync(P0210Fixture fixture, Guid? planId = null,
        Guid? source = null, int membersPerShop = 5, long monthlyPricePaise = 12000L)
    {
        var manager = fixture.CatalogueManager!;
        var plan = planId ?? Guid.NewGuid();
        var input = P0207Tests.PlanFacts(0);
        input["monthlyPricePaise"] = monthlyPricePaise;
        input["membersPerShop"] = membersPerShop;
        if (source is { } previous)
        {
            input["sourceVersionId"] = previous;
            input["sourceExpectedVersion"] = 2;
        }
        var draft = await P0207Tests.SavePlanAsync(manager, plan, input, HttpStatusCode.Created);
        var version = draft.GetProperty("versionId").GetGuid();
        var path = $"/api/admin/plans/{plan:D}/versions/{version:D}";
        var prepared = await P0207Tests.PrepareAsync(manager, path, Guid.NewGuid(), 1, fixture.PolicyId);
        using var published = await P0207Tests.ConfirmAsync(manager, path, prepared);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        return version;
    }
}
