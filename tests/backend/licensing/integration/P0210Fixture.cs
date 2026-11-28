using Microsoft.Extensions.Configuration;
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
    int policyRevision, P0110CookieBrowser? manager, string? managerSeed,
    bool hasPreappliedCheckoutGrants = false) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static readonly DateTimeOffset Start = P0209Fixture.Utc("2027-02-10T00:00:00.123456Z");
    internal P0209Fixture.Hosts Hosts { get; } = hosts;
    internal Guid PolicyId { get; } = policyId;
    internal int PolicyRevision { get; } = policyRevision;
    internal NpgsqlConnectionStringBuilder LicensingOwner => Hosts.LicensingOwner;
    internal P0206Fixture Catalogue => catalogue;
    internal string? CatalogueManagerSeed => managerSeed;
    internal P0110CookieBrowser? CatalogueManager => manager;
    internal bool HasPreappliedCheckoutGrants { get; } = hasPreappliedCheckoutGrants;

    internal static async Task<P0210Fixture> StartAsync(
        LicensingPostgreSqlFixture database,
        bool publish = true,
        bool browser = false,
        Func<HttpMessageHandler, HttpMessageHandler>? transport = null,
        bool approve = true,
        P0210HostSetup? setup = null,
        bool publishEntitlements = true)
    {
        await database.PrepareMerchantAsync();
        await LicensingPostgreSqlFixture.ExecuteAsync(
            database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase),
            "TRUNCATE public.rate_limit_buckets");
        var businessClock = setup?.BusinessClock ?? new P0201Clock { Now = Start };
        var prepareProvider = setup?.PrepareProvider != false;
        var useDelivery = setup?.Transport is not null;
        var liveDelivery = setup?.LiveDelivery == true;

        var (catalogue, manager, seed, policy, revision, usedTrialTemplate) = await CreateCatalogueAsync(
            database,
            businessClock,
            publish,
            browser,
            prepareProvider,
            useDelivery,
            liveDelivery,
            publishEntitlements,
            setup);

        var signing = new P0201Fixture(database, catalogue.Runtime.Database!);
        signing.Clock.Now = P0202Fixture.WholeSecondNow();
        var key = TestRsaKeys.Create(TestRsaKeyPurpose.ShopTrial);
        var settings = LicensingSettings(signing, catalogue, key, approve, browser, setup);

        var (licensing, ownsLicensing, clearLicensing) = CreateLicensingHost(
            database,
            signing,
            settings,
            businessClock,
            browser,
            prepareProvider,
            useDelivery,
            liveDelivery,
            publishEntitlements,
            setup);

        using var client = licensing.CreateClient();
        var (merchant, ownsMerchant, clearMerchant) = CreateMerchantHost(
            database,
            signing,
            licensing,
            client.BaseAddress!.AbsoluteUri,
            key,
            businessClock,
            transport,
            browser,
            prepareProvider,
            useDelivery,
            liveDelivery,
            setup);

        var hosts = new P0209Fixture.Hosts(
            signing,
            licensing,
            merchant,
            businessClock,
            database,
            OwnsLicensing: ownsLicensing,
            OwnsMerchant: ownsMerchant,
            OnDispose: async () =>
            {
                clearMerchant?.Invoke();
                if (clearLicensing is not null)
                    await clearLicensing();
            });
        return new(catalogue, key, hosts, policy, revision, manager, seed, hasPreappliedCheckoutGrants: usedTrialTemplate);
    }

    private static async Task<(P0206Fixture Catalogue, P0110CookieBrowser? Manager, string? Seed, Guid Policy, int Revision, bool UsedTrialTemplate)>
        CreateCatalogueAsync(
            LicensingPostgreSqlFixture database,
            P0201Clock businessClock,
            bool publish,
            bool browser,
            bool prepareProvider,
            bool useDelivery,
            bool liveDelivery,
            bool publishEntitlements,
            P0210HostSetup? setup)
    {
        var catalogueSettings = P0207Tests.LocalSettings();
        if (setup?.UseDeliveryOnCatalogue == true)
            foreach (var (name, value) in setup.LicensingSettings)
                catalogueSettings[name] = value;

        var useTrialTemplate = !database.IsIsolated
            && publish
            && prepareProvider
            && !browser
            && P0206Fixture.CanUseTemplateSettings(catalogueSettings);

        if (useTrialTemplate)
        {
            var (seededCatalogue, template) = await P0206Fixture.StartFromTrialTemplateAsync(
                database,
                catalogueSettings,
                businessClock,
                setup?.UseDeliveryOnCatalogue == true ? setup.CheckoutClock : null,
                useDelivery: setup?.UseDeliveryOnCatalogue == true && useDelivery,
                liveDelivery: liveDelivery,
                publishEntitlements: publishEntitlements,
                transport: setup?.Transport,
                traffic: setup?.LicensingTraffic,
                customConfigure: setup?.UseDeliveryOnCatalogue == true ? setup.ConfigureLicensing : null,
                razorpayHandler: setup?.UseDeliveryOnCatalogue == true ? setup.RazorpayHandler : null);
            var managerClient = seededCatalogue.Api.CreateClient(new()
            {
                BaseAddress = new Uri(P0110CookieBrowser.Origin),
                HandleCookies = false,
            });
            var seededManager = P0110CookieBrowser.FromSnapshot(managerClient, template.ManagerCookies!);
            return (
                seededCatalogue,
                seededManager,
                template.ManagerSeed,
                template.TrialPolicyId,
                template.TrialPolicyRevision,
                true);
        }

        var catalogue = await P0206Fixture.StartAsync(
            database,
            catalogueSettings,
            services =>
            {
                services.AddKeyedSingleton<TimeProvider>(P0209Fixture.StatusClockKey, businessClock);
                if (!publishEntitlements)
                    services.AddPreEntitlementPublisher();
                if (setup?.UseDeliveryOnCatalogue == true)
                    ConfigureLicensingExtensions(services, setup, useDelivery, liveDelivery);
            },
            prepareProvider: prepareProvider);
        var (manager, seed) = publish ? await catalogue.ManagerAsync() : (null, null);
        var policy = manager is not null ? await P0207Tests.PublishTrialAsync(manager, 1) : Guid.NewGuid();
        var revision = publish
            ? (int)(await catalogue.ScalarAsync($"SELECT revision FROM trial_policy_versions WHERE id = '{policy:D}'"))!
            : 2;
        return (catalogue, manager, seed, policy, revision, false);
    }

    private static (LicensingApiFactory Api, bool Owns, Func<ValueTask>? Clear) CreateLicensingHost(
        LicensingPostgreSqlFixture database,
        P0201Fixture signing,
        Dictionary<string, string?> settings,
        P0201Clock businessClock,
        bool browser,
        bool prepareProvider,
        bool useDelivery,
        bool liveDelivery,
        bool publishEntitlements,
        P0210HostSetup? setup)
    {
        if (!database.IsIsolated && !browser && prepareProvider && setup?.ConfigureLicensing is null)
        {
            var pooled = database.HostPool.AcquireLicensingHost(
                signing.Runtime,
                settings,
                signing.Clock,
                businessClock,
                setup?.CheckoutClock,
                useDelivery,
                liveDelivery,
                publishEntitlements,
                setup?.Transport,
                setup?.LicensingTraffic,
                setup?.RazorpayHandler);
            return (pooled.Api, false, () => database.HostPool.ReleaseLicensingHostAsync(pooled));
        }

        var licensing = P0202Fixture.LicensingApi(
            signing,
            configure: services =>
            {
                services.AddKeyedSingleton<TimeProvider>(P0209Fixture.StatusClockKey, businessClock);
                services.AddKeyedSingleton<TimeProvider>(ShopTrialsV1.DeliverySection, businessClock);
                if (!publishEntitlements)
                    services.AddPreEntitlementPublisher();
                if (setup is not null)
                    ConfigureLicensingExtensions(services, setup, useDelivery, liveDelivery);
            },
            clock: prepareProvider ? null : TimeProvider.System,
            settings: settings);
        if (browser)
            licensing.UseKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0));
        return (licensing, true, null);
    }

    private static (MerchantStartupProbeFactory Api, bool Owns, Action? Clear) CreateMerchantHost(
        LicensingPostgreSqlFixture database,
        P0201Fixture signing,
        LicensingApiFactory licensing,
        string browserOrigin,
        RSA key,
        P0201Clock businessClock,
        Func<HttpMessageHandler, HttpMessageHandler>? transport,
        bool browser,
        bool prepareProvider,
        bool useDelivery,
        bool liveDelivery,
        P0210HostSetup? setup)
    {
        var merchantSettings = new Dictionary<string, string?>
        {
            [$"{ShopTrialsV1.DeliverySection}:LocalPrivateKeyPem"] = key.ExportPkcs8PrivateKeyPem(),
            [$"{ShopTrialsV1.DeliverySection}:LocalKeyId"] = "trial-fixture",
        };
        if (setup is not null)
            foreach (var (name, value) in setup.MerchantSettings)
                merchantSettings[name] = value;

        if (!database.IsIsolated && !browser && prepareProvider && setup?.ConfigureMerchant is null)
        {
            var delegationHandler = transport?.Invoke(licensing.Server.CreateHandler()) ?? licensing.Server.CreateHandler();
            var pooled = database.HostPool.AcquireMerchantHost(
                signing,
                merchantSettings,
                signing.Clock,
                businessClock,
                delegationHandler,
                useDelivery,
                liveDelivery,
                setup?.Transport,
                setup?.MerchantTraffic);
            return (pooled.Api, false, () =>
            {
                pooled.DelegationHandler.Set(null);
                pooled.EntitlementHandler.Set(null);
                pooled.Traffic.Set(null);
            });
        }

        var merchant = signing.MerchantApi(
            browser ? browserOrigin : "https://licensing.test",
            handler: browser ? null : () => transport?.Invoke(licensing.Server.CreateHandler()) ?? licensing.Server.CreateHandler(),
            configure: services =>
            {
                services.AddSingleton<TimeProvider>(prepareProvider ? signing.Clock : TimeProvider.System);
                services.AddKeyedSingleton<TimeProvider>(ShopTrialsV1.DeliverySection, businessClock);
                if (useDelivery)
                    P0213Fixture.Configure(services, setup!.Transport!, setup.MerchantTraffic!, licensing: false, liveDelivery);
                setup?.ConfigureMerchant?.Invoke(services);
            },
            settings: merchantSettings);
        if (browser)
            merchant.UseKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0));
        return (merchant, true, null);
    }

    private static void ConfigureLicensingExtensions(
        IServiceCollection services,
        P0210HostSetup setup,
        bool useDelivery,
        bool liveDelivery)
    {
        if (setup.CheckoutClock is { } checkoutClock)
        {
            services.AddKeyedSingleton<TimeProvider>(CheckoutOrdersV1.Policy, checkoutClock);
            services.AddKeyedSingleton<TimeProvider>("pending-payment-lease", checkoutClock);
        }
        if (useDelivery)
            P0213Fixture.Configure(services, setup.Transport!, setup.LicensingTraffic!, licensing: true, liveDelivery);
        if (setup.RazorpayHandler is { } razorpayHandler)
            services.AddHttpClient("razorpay-gateway").ConfigurePrimaryHttpMessageHandler(() => razorpayHandler);
        setup.ConfigureLicensing?.Invoke(services);
    }

    private static Dictionary<string, string?> LicensingSettings(P0201Fixture signing, P0206Fixture catalogue,
        RSA key, bool approve, bool browser, P0210HostSetup? setup)
    {
        var settings = signing.LicensingSettings();
        if (approve)
            foreach (var (name, value) in P0207Tests.LocalSettings()) settings[name] = value;
        settings[$"{ShopTrialsV1.DeliverySection}:PublicKeys:trial-fixture"] = key.ExportSubjectPublicKeyInfoPem();
        settings["LicensingDatabase:MaxPoolSize"] = "8";
        if (browser)
        {
            var admin = catalogue.Api.Services.GetRequiredService<IConfiguration>();
            settings["AdminMfa:ActiveKeyId"] = admin["AdminMfa:ActiveKeyId"];
            foreach (var material in admin.GetSection("AdminMfa:Keys").GetChildren()) settings[material.Path] = material.Value;
            settings["AdminSessions:SigningKey"] = admin["AdminSessions:SigningKey"];
        }
        if (setup is not null)
            foreach (var (name, value) in setup.LicensingSettings) settings[name] = value;
        return settings;
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


internal sealed record P0210HostSetup(
    IReadOnlyDictionary<string, string?> LicensingSettings,
    IReadOnlyDictionary<string, string?> MerchantSettings,
    Action<IServiceCollection>? ConfigureLicensing,
    Action<IServiceCollection>? ConfigureMerchant,
    bool PrepareProvider = true,
    bool UseDeliveryOnCatalogue = false,
    P0201Clock? BusinessClock = null,
    P0213Transport? Transport = null,
    P0213Traffic? LicensingTraffic = null,
    P0213Traffic? MerchantTraffic = null,
    bool LiveDelivery = false,
    P0201Clock? CheckoutClock = null,
    HttpMessageHandler? RazorpayHandler = null);
