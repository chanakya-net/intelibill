using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// Renewal fixture over the real simulator purchase pipeline. Quote/acceptance/capture and fulfilment/status use the
/// same fixed clock so every boundary is exact; identity, MFA and
/// delivery signing keep their own clocks.
/// </summary>
internal sealed class P0308Fixture(P0302Fixture checkout) : IAsyncDisposable
{
    internal P0302Fixture Checkout { get; } = checkout;
    internal P0209Fixture.Hosts Hosts => Checkout.Hosts;

    internal static async Task<P0308Fixture> StartAsync(LicensingPostgreSqlFixture database, DateTimeOffset at,
        IReadOnlyDictionary<string, string?>? settings = null, bool browser = false, Action<IServiceCollection>? services = null)
    {
        var checkout = await P0302Fixture.StartAsync(database, new P0201Clock { Now = at }, settings, services, browser);
        var fixture = new P0308Fixture(checkout);
        fixture.At(at);
        return fixture;
    }

    /// <summary>Moves the shared checkout and lifecycle clock to one instant.</summary>
    internal void At(DateTimeOffset at)
    {
        Checkout.CheckoutClock.Now = at;
    }

    internal async Task<Package> PackageAsync(int count, string[]? names = null, int? tradingIndex = null)
    {
        var payer = await SignInAsync(Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(payer.Client, "Renewal Fixture");
        var shops = new List<Merchant> { payer };
        for (var index = 1; index < count; index++)
            shops.Add(index == tradingIndex ? await CreateTradingShopAsync(Hosts) : await SignInAsync(Hosts, ShopRole.Owner));
        foreach (var (shop, index) in shops.Select((shop, index) => (shop, index)))
        {
            if (names is not null)
                await LicensingPostgreSqlFixture.ExecuteAsync(Checkout.Delivery.MerchantDb,
                    $"UPDATE shops SET name = '{names[index]}' WHERE id = '{shop.ShopId:D}'");
            await P0213Fixture.LinkAsync(account, payer, shop);
        }
        await Checkout.Delivery.DrainAsync(source: true);
        await Checkout.Delivery.DrainAsync(source: false);
        var plan = await P0301Tests.PublishPlanAsync(Checkout.Trial);
        return new(payer, account, [.. shops], plan);
    }

    internal static Task<CheckoutQuoteV1> FirstQuoteAsync(Package package, string period) =>
        P0301Tests.CreateQuoteAsync(package.Payer.Client, package.Account, new(Guid.NewGuid(), package.Plan, period,
            [.. package.Shops.Select(shop => shop.ShopId)], 0, 1));

    internal static Task<CheckoutQuoteV1> RenewalQuoteAsync(Package package, Guid term, string period) =>
        P0301Tests.CreateQuoteAsync(package.Payer.Client, package.Account,
            new(Guid.NewGuid(), package.Plan, period, [], 0, 1, term));

    internal static async Task<HttpResponseMessage> TryRenewalQuoteAsync(Package package, Guid term, string period) =>
        await package.Payer.Client.PostAsJsonAsync(P0301Tests.OrdersPath(package.Account),
            new CreateCheckoutQuoteV1(Guid.NewGuid(), package.Plan, period, [], 0, 1, term));

    internal static async Task<CheckoutQuoteV1> AcceptAsync(Package package, CheckoutQuoteV1 quote)
    {
        using var accepted = await package.Payer.Client.PostAsJsonAsync(P0301Tests.AcceptPath(quote), P0301Tests.Accept(quote));
        Assert.True(accepted.StatusCode == HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        return (await accepted.Content.ReadFromJsonAsync<CheckoutQuoteV1>())!;
    }

    /// <summary>Accepted quote → started attempt → simulator capture now → server verification.</summary>
    internal static async Task<JsonElement> PayAsync(Package package, CheckoutQuoteV1 quote)
    {
        var attempt = await StartPaymentAsync(package, quote);
        await P0302Fixture.CaptureAsync(package.Payer.Client, quote, attempt);
        return await P0302Fixture.VerifyAsync(package.Payer.Client, quote, attempt);
    }

    internal static async Task<Guid> StartPaymentAsync(Package package, CheckoutQuoteV1 quote) =>
        (await P0302Fixture.StartAsync(package.Payer.Client, quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();

    internal static async Task<(CheckoutQuoteV1 Quote, Guid Term)> PurchaseAsync(Package package, string period)
    {
        var quote = await AcceptAsync(package, await FirstQuoteAsync(package, period));
        var paid = await PayAsync(package, quote);
        P0302Fixture.AssertStatus(paid, "Verified", "Fulfilled", "Applying");
        return (quote, paid.GetProperty("termId").GetGuid());
    }

    internal static async Task<(CheckoutQuoteV1 Quote, JsonElement Status)> RenewAsync(Package package, Guid term, string period)
    {
        var quote = await AcceptAsync(package, await RenewalQuoteAsync(package, term, period));
        return (quote, await PayAsync(package, quote));
    }

    internal static async Task<JsonElement> ShopTermAsync(Package package, Merchant shop) =>
        (await ReadAsync(await ShopStatusAsync(shop.Client, shop.ShopId))).GetProperty("term");

    internal async Task<object?> ScalarAsync(string sql) =>
        await LicensingPostgreSqlFixture.ReadScalarAsync(Hosts.LicensingOwner, sql);

    internal Task<long> CountAsync(string table, string where = "true") => Checkout.CountAsync(table, where);

    public ValueTask DisposeAsync() => Checkout.DisposeAsync();

    internal sealed record Package(Merchant Payer, Guid Account, Merchant[] Shops, Guid Plan);
}
