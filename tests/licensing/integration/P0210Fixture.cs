using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

internal sealed class P0210Fixture(P0206Fixture catalogue, RSA deliveryKey, P0209Fixture.Hosts hosts, Guid policyId,
    int policyRevision, P0110CookieBrowser? manager) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static readonly DateTimeOffset Start = P0209Fixture.Utc("2027-02-10T00:00:00.123456Z");
    internal P0209Fixture.Hosts Hosts { get; } = hosts;
    internal Guid PolicyId { get; } = policyId;
    internal int PolicyRevision { get; } = policyRevision;
    internal NpgsqlConnectionStringBuilder LicensingOwner => Hosts.LicensingOwner;
    internal P0110CookieBrowser? CatalogueManager => manager;

    internal static async Task<P0210Fixture> StartAsync(LicensingPostgreSqlFixture database, bool publish = true, bool browser = false,
        Func<HttpMessageHandler, HttpMessageHandler>? transport = null, bool approve = true)
    {
        await database.PrepareMerchantAsync();
        await LicensingPostgreSqlFixture.ExecuteAsync(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase),
            "TRUNCATE public.rate_limit_buckets");
        var catalogue = await P0206Fixture.StartAsync(database, P0207Tests.LocalSettings());
        var manager = publish ? (await catalogue.ManagerAsync()).Manager : null;
        var policy = manager is not null ? await P0207Tests.PublishTrialAsync(manager, 1) : Guid.NewGuid();
        var revision = publish ? (int)(await catalogue.ScalarAsync($"SELECT revision FROM trial_policy_versions WHERE id = '{policy:D}'"))! : 2;
        var signing = new P0201Fixture(database, catalogue.Runtime.Database!);
        signing.Clock.Now = P0202Fixture.WholeSecondNow();
        var businessClock = new P0201Clock { Now = Start };
        var key = RSA.Create(2048);
        var settings = signing.LicensingSettings();
        if (approve)
            foreach (var (name, value) in P0207Tests.LocalSettings()) settings[name] = value;
        settings[$"{ShopTrialsV1.DeliverySection}:PublicKeys:trial-fixture"] = key.ExportSubjectPublicKeyInfoPem();
        settings["LicensingDatabase:MaxPoolSize"] = "8";
        var licensing = P0202Fixture.LicensingApi(signing, configure: services =>
        {
            services.AddKeyedSingleton<TimeProvider>(P0209Fixture.StatusClockKey, businessClock);
            services.AddKeyedSingleton<TimeProvider>(ShopTrialsV1.DeliverySection, businessClock);
        }, settings: settings);
        if (browser)
            licensing.UseKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0));
        using var client = licensing.CreateClient();
        var merchant = signing.MerchantApi(browser ? client.BaseAddress!.AbsoluteUri : "https://licensing.test",
            handler: browser ? null : () => transport?.Invoke(licensing.Server.CreateHandler()) ?? licensing.Server.CreateHandler(),
            configure: services =>
            {
                services.AddSingleton<TimeProvider>(signing.Clock);
                services.AddKeyedSingleton<TimeProvider>(ShopTrialsV1.DeliverySection, businessClock);
            }, settings: new Dictionary<string, string?>
            {
                [$"{ShopTrialsV1.DeliverySection}:LocalPrivateKeyPem"] = key.ExportPkcs8PrivateKeyPem(),
                [$"{ShopTrialsV1.DeliverySection}:LocalKeyId"] = "trial-fixture",
            });
        if (browser)
            merchant.UseKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0));
        return new(catalogue, key, new(signing, licensing, merchant, businessClock, database), policy, revision, manager);
    }

    internal async Task LinkAsync(Guid account, Guid payer, P0209Fixture.Merchant shop) =>
        await P0209Fixture.AffiliateAsync(Hosts, account, payer, shop.ShopId, shop.UserId, null, Start.AddDays(-1), null);

    internal async Task<long> CountAsync(string table) =>
        (long)(await LicensingPostgreSqlFixture.ReadScalarAsync(LicensingOwner, $"SELECT count(*) FROM public.{table}"))!;

    internal ShopTrialAcceptanceV1 Accept(Guid? key = null) =>
        new(key ?? Guid.NewGuid(), PolicyId, PolicyRevision, "trial-local-1", true);

    internal HttpRequestMessage Delivery(ShopTrialConsentV1 consent, Guid? replay = null)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(consent, Json);
        var now = Hosts.Fixture.Clock.GetUtcNow().ToUnixTimeSeconds();
        var header = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            alg = "RS256", typ = ShopTrialsV1.DeliveryType, kid = "trial-fixture",
        }));
        var claims = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            v = 1, iss = ShopTrialsV1.DeliveryIssuer, aud = ShopTrialsV1.DeliveryAudience,
            jti = replay ?? Guid.NewGuid(), iat = now, nbf = now, exp = now + 60,
            method = "POST", path = ShopTrialsV1.DeliveryPath, body_sha256 = Convert.ToHexStringLower(SHA256.HashData(body)),
        }));
        var input = $"{header}.{claims}";
        var signature = deliveryKey.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ShopTrialsV1.DeliveryPath, UriKind.Relative))
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", $"{input}.{Base64UrlEncoder.Encode(signature)}");
        return request;
    }

    public async ValueTask DisposeAsync()
    {
        await Hosts.DisposeAsync();
        manager?.Dispose();
        deliveryKey.Dispose();
        await catalogue.DisposeAsync();
    }
}
