using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Integration.Tests;

[Collection(MerchantBillingHosts.Name)]
public sealed partial class P0205Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task NamedLiveOwner_GrantsDurableConsentForExactRequest()
    {
        await using var host = await StartAsync();
        using var payer = host.Merchant.CreateClient();
        using var owner = host.Merchant.CreateClient();
        await SignInAsync(host.Merchant, payer);
        var shop = await SignInAsync(host.Merchant, owner, withShop: true);
        var account = await CreateAccountAsync(payer);
        var request = await RequestAsync(payer, account, shop);
        var path = ConsentPath(account, shop, request);
        using var denied = await payer.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var granted = await owner.PostAsJsonAsync(path + "/grant", new { operationKey = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        Assert.Equal("Granted", (await owner.GetFromJsonAsync<JsonElement>(path)).GetProperty("status").GetString());
    }

    private async Task<Hosts> StartAsync(Func<HttpMessageHandler, HttpMessageHandler>? transport = null, RSA? deliveryKey = null)
    {
        await database.PrepareMerchantAsync();
        await LicensingPostgreSqlFixture.ExecuteAsync(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase),
            "TRUNCATE public.rate_limit_buckets");
        var fixture = await P0201Fixture.CreateAsync(database);
        fixture.Clock.Now = P0202Fixture.WholeSecondNow();
        using var generatedKey = deliveryKey is null ? RSA.Create(2048) : null;
        var delivery = deliveryKey ?? generatedKey!;
        var licensing = P0202Fixture.LicensingApi(fixture, settings: LicensingSettings(fixture, delivery));
        var merchant = fixture.MerchantApi(handler: () => transport?.Invoke(licensing.Server.CreateHandler()) ?? licensing.Server.CreateHandler(),
            configure: services => services.AddSingleton<TimeProvider>(fixture.Clock), settings: MerchantSettings(delivery));
        return new Hosts(fixture, licensing, merchant);
    }

    internal static Dictionary<string, string?> LicensingSettings(P0201Fixture fixture, RSA key)
    {
        var settings = fixture.LicensingSettings();
        settings["ShopAffiliationDelivery:PublicKeys:local-affiliation"] = key.ExportSubjectPublicKeyInfoPem();
        return settings;
    }

    internal static Dictionary<string, string?> MerchantSettings(RSA key) => new()
    {
        ["ShopAffiliationDelivery:LocalKeyId"] = "local-affiliation",
        ["ShopAffiliationDelivery:LocalPrivateKeyPem"] = key.ExportPkcs8PrivateKeyPem(),
    };

    private async Task<Guid> SignInAsync(MerchantStartupProbeFactory merchant, HttpClient client, bool withShop = false)
    {
        var email = P0202Fixture.UniqueEmail();
        var user = await P0201Tests.SeedMerchantAsync(merchant.Services, email);
        var shopId = Guid.Empty;
        if (withShop)
        {
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase).ConnectionString)
                .UseSnakeCaseNamingConvention().Options);
            var shop = Shop.Create("Fixture Shop", "Fixture Road", "Bengaluru", "Karnataka", "560001", null, null, null);
            db.Shops.Add(shop);
            db.ShopMemberships.Add(ShopMembership.Create(shop.Id, user, ShopRole.Owner, true));
            await db.SaveChangesAsync();
            shopId = shop.Id;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await P0201Tests.LoginAsync(client, email));
        return shopId;
    }

    private static async Task<Guid> CreateAccountAsync(HttpClient payer)
    {
        using var response = await payer.PostAsJsonAsync("/api/billing/accounts", new { operationKey = Guid.NewGuid(), name = "Fixture Payer" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accountId").GetGuid();
    }

    private static async Task<Guid> RequestAsync(HttpClient payer, Guid account, Guid shop, Guid? termId = null)
    {
        using var response = await payer.PostAsJsonAsync($"/api/billing/accounts/{account:D}/affiliations", new
        {
            operationKey = Guid.NewGuid(), shopId = shop, termId, withoutCoverage = termId is null,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestId").GetGuid();
    }

    private static string ConsentPath(Guid account, Guid shop, Guid request) =>
        $"/api/billing/accounts/{account:D}/affiliations/{shop:D}/{request:D}/consent";

    private sealed record Hosts(P0201Fixture Fixture, LicensingApiFactory Licensing, MerchantStartupProbeFactory Merchant) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Merchant.DisposeAsync();
            await Licensing.DisposeAsync();
            Fixture.Dispose();
        }
    }
}
