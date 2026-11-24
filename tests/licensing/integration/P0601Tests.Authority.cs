using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0601Tests
{
    /// <summary>
    /// Two concurrent requests with one key freeze one quote; a changed payload is refused; an expired replay returns
    /// the original timestamps. Only the live billing owner may quote or page its shops.
    /// </summary>
    [Fact]
    public async Task ConcurrentRetryRecoversOnlyTheAuthorizedFrozenQuote()
    {
        await using var fixture = await StartAsync(database, January31);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (_, term) = await PurchaseAsync(package, "Month");
        fixture.At(February14);
        var key = Guid.NewGuid();
        var request = Request(package.Plan, 4, 3, February29, key);

        var both = await Task.WhenAll(QuoteAsync(package, term, request), QuoteAsync(package, term, request));
        Assert.Equal(both[0].GetProperty("quoteId").GetGuid(), both[1].GetProperty("quoteId").GetGuid());
        Assert.Equal(1L, await fixture.CountAsync("capacity_change_quotes"));
        var original = both[0];
        Assert.Equal(15000L, original.GetProperty("differencePaise").GetInt64());

        using var changed = await PostQuoteAsync(package, term, Request(package.Plan, 5, 3, February29, key));
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal("Billing.OperationPayloadMismatch", await P0206Fixture.CodeAsync(changed));

        fixture.At(February14.AddMinutes(20));
        var expired = await QuoteAsync(package, term, request);
        Assert.Equal(original.GetProperty("snapshotHash").GetString(), expired.GetProperty("snapshotHash").GetString());
        Assert.Equal(original.GetProperty("expiresAt").GetDateTimeOffset(), expired.GetProperty("expiresAt").GetDateTimeOffset());
        var fresh = await QuoteAsync(package, term, Request(package.Plan, 4, 3, February29));
        Assert.NotEqual(original.GetProperty("quoteId").GetGuid(), fresh.GetProperty("quoteId").GetGuid());
        Assert.Equal(February14.AddMinutes(20), fresh.GetProperty("evaluatedAt").GetDateTimeOffset());
        Assert.Equal(2L, await fixture.CountAsync("capacity_change_quotes"));
        Assert.Equal(4L, await fixture.CountAsync("capacity_change_quote_shops"));
    }

    [Fact]
    public async Task OnlyTheLiveBillingOwnerQuotesOrReadsFrozenShops()
    {
        await using var fixture = await StartAsync(database, January31);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (_, term) = await PurchaseAsync(package, "Month");
        fixture.At(February14);
        var quote = await QuoteAsync(package, term, Request(package.Plan, 4, 3, February29));
        var shopsPath = $"{QuotesPath(package.Account, term)}/{quote.GetProperty("quoteId").GetGuid():D}/shops";

        var manager = package.Shops[1];
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            INSERT INTO billing_account_members (id, account_id, merchant_user_id, role, status, revision, created_at)
            VALUES ('{Guid.NewGuid():D}', '{package.Account:D}', '{manager.UserId:D}', 2, 1, 1, now())
            """);
        var stranger = await P0209Fixture.SignInAsync(fixture.Hosts, Intelibill.Domain.Enums.ShopRole.Owner);
        foreach (var client in new[] { manager.Client, stranger.Client })
        {
            using var create = await client.PostAsJsonAsync(QuotesPath(package.Account, term), Request(package.Plan, 4, 3, February29));
            Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
            using var page = await client.GetAsync(new Uri(shopsPath, UriKind.Relative));
            Assert.Equal(HttpStatusCode.Forbidden, page.StatusCode);
        }
        using var foreignQuote = await package.Payer.Client.GetAsync(new Uri(
            $"{QuotesPath(package.Account, term)}/{Guid.NewGuid():D}/shops", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, foreignQuote.StatusCode);
        Assert.Equal(ShopNames.Order(), (await ShopsAsync(package, term, quote.GetProperty("quoteId").GetGuid())).Order());
        Assert.Equal(1L, await fixture.CountAsync("capacity_change_quotes"));
        Assert.Equal(JsonValueKind.False, quote.GetProperty("suspensionDisclosed").ValueKind);
    }
}
