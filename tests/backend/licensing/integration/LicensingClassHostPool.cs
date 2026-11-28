using System.Text;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Web.Delivery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// Reuses ASP.NET Core test hosts across sequential tests within one xUnit test class. Each test still
/// gets its own isolated licensing PostgreSQL database, clocks, mailer, HTTP bridges and traffic counters;
/// only the expensive host/DI/endpoint-compilation lifecycle is amortized across the class.
/// </summary>
internal sealed class LicensingClassHostPool : IAsyncDisposable
{
    private const string FixedAdminFixtureId = "11111111-1111-1111-1111-111111111111";

    private readonly Dictionary<string, PooledCatalogueHost> _catalogueHosts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PooledLicensingHost> _licensingHosts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PooledMerchantHost> _merchantHosts = new(StringComparer.Ordinal);

    internal PooledCatalogueHost AcquireCatalogueHost(
        NpgsqlConnectionStringBuilder runtime,
        Dictionary<string, string?> settings,
        P0106Tests.TestClock clock,
        P0114Tests.FixtureMailer mailer,
        TimeProvider? statusClock,
        TimeProvider? checkoutClock,
        bool useDelivery,
        bool liveDelivery,
        bool publishEntitlements,
        P0213Transport? transport,
        P0213Traffic? traffic,
        HttpMessageHandler? razorpayHandler = null)
    {
        var useRazorpayHandler = razorpayHandler is not null;
        var key = BuildKey(useRazorpayHandler ? "cat-rzp" : "cat", settings, useDelivery, liveDelivery, publishEntitlements);
        var pooledConn = BuildPooledLicensingConnectionString(runtime, maxPoolSize: 8);
        if (!_catalogueHosts.TryGetValue(key, out var host))
        {
            var hostSettings = new Dictionary<string, string?>(settings, StringComparer.Ordinal)
            {
                ["AdminFixtures:AdminId"] = FixedAdminFixtureId,
            };
            var dbSlot = new SwappableLicensingDatabase(pooledConn);
            var clockSlot = new SwappableTimeProvider(clock);
            var statusClockSlot = new SwappableTimeProvider(statusClock ?? clock);
            var checkoutClockSlot = new SwappableTimeProvider(checkoutClock ?? clock);
            var pendingLeaseClockSlot = new SwappableTimeProvider(checkoutClock ?? TimeProvider.System);
            var leaseClockSlot = new SwappableTimeProvider((TimeProvider?)transport?.LeaseClock ?? clock);
            var mailerSlot = new SwappableAdminMailer();
            var entitlementSlot = new SwappableHttpMessageHandler();
            var razorpaySlot = new SwappableHttpMessageHandler();
            var trafficSlot = new SwappableTrafficFilter();

            var api = new LicensingApiFactory(
                runtime,
                clock: clockSlot,
                settings: hostSettings,
                databaseSlot: dbSlot,
                configure: services =>
                {
                    services.AddSingleton<IAdminInvitationMailer>(mailerSlot);
                    services.AddKeyedSingleton<TimeProvider>(P0209Fixture.StatusClockKey, statusClockSlot);
                    services.AddKeyedSingleton<TimeProvider>(CheckoutOrdersV1.Policy, checkoutClockSlot);
                    services.AddKeyedSingleton<TimeProvider>("pending-payment-lease", pendingLeaseClockSlot);
                    if (!publishEntitlements)
                        services.AddPreEntitlementPublisher();
                    if (useDelivery)
                        ConfigureDeliveryShims(services, entitlementSlot, leaseClockSlot, trafficSlot, liveDelivery);
                    if (useRazorpayHandler)
                        services.AddHttpClient("razorpay-gateway").ConfigurePrimaryHttpMessageHandler(() => razorpaySlot);
                });

            host = new PooledCatalogueHost(
                key,
                api,
                dbSlot,
                clockSlot,
                statusClockSlot,
                checkoutClockSlot,
                pendingLeaseClockSlot,
                leaseClockSlot,
                mailerSlot,
                entitlementSlot,
                razorpaySlot,
                trafficSlot,
                SnapshotSettings(hostSettings));
            _catalogueHosts[key] = host;
        }

        host.Database.Set(pooledConn);
        host.Clock.Set(clock);
        host.StatusClock.Set(statusClock ?? clock);
        host.CheckoutClock.Set(checkoutClock ?? clock);
        host.PendingLeaseClock.Set(checkoutClock ?? TimeProvider.System);
        host.LeaseClock.Set((TimeProvider?)transport?.LeaseClock ?? clock);
        host.Mailer.Set(mailer);
        host.EntitlementHandler.Set(useDelivery && transport is not null ? transport.Bridge(licensing: true) : null);
        host.RazorpayHandler.Set(razorpayHandler);
        host.Traffic.Set(traffic);
        return host;
    }

    internal PooledLicensingHost AcquireLicensingHost(
        NpgsqlConnectionStringBuilder runtime,
        Dictionary<string, string?> settings,
        TimeProvider clock,
        TimeProvider businessClock,
        TimeProvider? checkoutClock,
        bool useDelivery,
        bool liveDelivery,
        bool publishEntitlements,
        P0213Transport? transport,
        P0213Traffic? traffic,
        HttpMessageHandler? razorpayHandler = null)
    {
        var useRazorpayHandler = razorpayHandler is not null;
        var key = BuildKey(useRazorpayHandler ? "lic-rzp" : "lic", settings, useDelivery, liveDelivery, publishEntitlements);
        var maxPool = settings.TryGetValue("LicensingDatabase:MaxPoolSize", out var raw) && int.TryParse(raw, out var parsed)
            ? parsed
            : 8;
        var pooledConn = BuildPooledLicensingConnectionString(runtime, maxPool);
        if (!_licensingHosts.TryGetValue(key, out var host))
        {
            var dbSlot = new SwappableLicensingDatabase(pooledConn);
            var clockSlot = new SwappableTimeProvider(clock);
            var statusClockSlot = new SwappableTimeProvider(businessClock);
            var trialClockSlot = new SwappableTimeProvider(businessClock);
            var checkoutClockSlot = new SwappableTimeProvider(checkoutClock ?? clock);
            var pendingLeaseClockSlot = new SwappableTimeProvider(checkoutClock ?? TimeProvider.System);
            var leaseClockSlot = new SwappableTimeProvider((TimeProvider?)transport?.LeaseClock ?? clock);
            var entitlementSlot = new SwappableHttpMessageHandler();
            var razorpaySlot = new SwappableHttpMessageHandler();
            var trafficSlot = new SwappableTrafficFilter();

            var api = new LicensingApiFactory(
                runtime,
                clock: clockSlot,
                settings: settings,
                databaseSlot: dbSlot,
                configure: services =>
                {
                    services.AddKeyedSingleton<TimeProvider>(P0209Fixture.StatusClockKey, statusClockSlot);
                    services.AddKeyedSingleton<TimeProvider>(ShopTrialsV1.DeliverySection, trialClockSlot);
                    services.AddKeyedSingleton<TimeProvider>(CheckoutOrdersV1.Policy, checkoutClockSlot);
                    services.AddKeyedSingleton<TimeProvider>("pending-payment-lease", pendingLeaseClockSlot);
                    if (!publishEntitlements)
                        services.AddPreEntitlementPublisher();
                    if (useDelivery)
                        ConfigureDeliveryShims(services, entitlementSlot, leaseClockSlot, trafficSlot, liveDelivery);
                    if (useRazorpayHandler)
                        services.AddHttpClient("razorpay-gateway").ConfigurePrimaryHttpMessageHandler(() => razorpaySlot);
                });

            host = new PooledLicensingHost(
                key,
                api,
                dbSlot,
                clockSlot,
                statusClockSlot,
                trialClockSlot,
                checkoutClockSlot,
                pendingLeaseClockSlot,
                leaseClockSlot,
                entitlementSlot,
                razorpaySlot,
                trafficSlot,
                SnapshotSettings(settings));
            _licensingHosts[key] = host;
        }

        host.Database.Set(pooledConn);
        host.Clock.Set(clock);
        host.StatusClock.Set(businessClock);
        host.TrialClock.Set(businessClock);
        host.CheckoutClock.Set(checkoutClock ?? clock);
        host.PendingLeaseClock.Set(checkoutClock ?? TimeProvider.System);
        host.LeaseClock.Set((TimeProvider?)transport?.LeaseClock ?? clock);
        host.EntitlementHandler.Set(useDelivery && transport is not null ? transport.Bridge(licensing: true) : null);
        host.RazorpayHandler.Set(razorpayHandler);
        host.Traffic.Set(traffic);
        return host;
    }

    internal PooledMerchantHost AcquireMerchantHost(
        P0201Fixture signing,
        Dictionary<string, string?> settings,
        TimeProvider clock,
        TimeProvider businessClock,
        HttpMessageHandler delegationHandler,
        bool useDelivery,
        bool liveDelivery,
        P0213Transport? transport,
        P0213Traffic? traffic)
    {
        var key = BuildKey("mer", settings, useDelivery, liveDelivery, flag3: false);
        if (!_merchantHosts.TryGetValue(key, out var host))
        {
            var clockSlot = new SwappableTimeProvider(clock);
            var trialClockSlot = new SwappableTimeProvider(businessClock);
            var leaseClockSlot = new SwappableTimeProvider((TimeProvider?)transport?.LeaseClock ?? clock);
            var delegationSlot = new SwappableHttpMessageHandler();
            var entitlementSlot = new SwappableHttpMessageHandler();
            var trafficSlot = new SwappableTrafficFilter();

            var api = signing.MerchantApi(
                origin: "https://licensing.test",
                handler: () => delegationSlot,
                configure: services =>
                {
                    services.AddSingleton<TimeProvider>(clockSlot);
                    services.AddKeyedSingleton<TimeProvider>(ShopTrialsV1.DeliverySection, trialClockSlot);
                    if (useDelivery)
                        ConfigureDeliveryShims(services, entitlementSlot, leaseClockSlot, trafficSlot, liveDelivery);
                },
                settings: settings);

            host = new PooledMerchantHost(
                api,
                clockSlot,
                trialClockSlot,
                leaseClockSlot,
                delegationSlot,
                entitlementSlot,
                trafficSlot);
            _merchantHosts[key] = host;
        }

        host.Clock.Set(clock);
        host.TrialClock.Set(businessClock);
        host.LeaseClock.Set((TimeProvider?)transport?.LeaseClock ?? clock);
        host.DelegationHandler.Set(delegationHandler);
        host.EntitlementHandler.Set(useDelivery && transport is not null ? transport.Bridge(licensing: false) : null);
        host.Traffic.Set(traffic);
        return host;
    }

    internal static string BuildPooledLicensingConnectionString(NpgsqlConnectionStringBuilder runtime, int maxPoolSize) =>
        new NpgsqlConnectionStringBuilder(runtime.ConnectionString)
        {
            Pooling = true,
            MaxPoolSize = maxPoolSize,
            Timeout = 30,
            CommandTimeout = 30,
        }.ConnectionString;

    private static void ConfigureDeliveryShims(
        IServiceCollection services,
        SwappableHttpMessageHandler entitlementSlot,
        SwappableTimeProvider leaseClockSlot,
        SwappableTrafficFilter trafficSlot,
        bool liveDelivery)
    {
        var worker = services.Single(service => service.ServiceType == typeof(IHostedService)
            && service.ImplementationType == typeof(LeasedHttpDeliveryWorker));
        if (!liveDelivery)
        {
            services.Remove(worker);
            services.AddKeyedSingleton<TimeProvider>(LeasedHttpDeliveryPump.LeaseClockKey, leaseClockSlot);
        }

        services.AddHttpClient(LeasedHttpDeliveryPump.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => entitlementSlot);
        services.AddSingleton<IStartupFilter>(trafficSlot);
    }

    private static string BuildKey(
        string prefix,
        IReadOnlyDictionary<string, string?> settings,
        bool flag1,
        bool flag2,
        bool flag3)
    {
        var builder = new StringBuilder(prefix)
            .Append('|').Append(flag1)
            .Append('|').Append(flag2)
            .Append('|').Append(flag3);
        foreach (var pair in settings.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (pair.Key.StartsWith("LicensingDatabase:", StringComparison.Ordinal)
                && pair.Key != "LicensingDatabase:MaxPoolSize")
                continue;
            if (pair.Key == "AdminFixtures:AdminId")
                continue;
            builder.Append('\n').Append(pair.Key).Append('=').Append(pair.Value);
        }

        return builder.ToString();
    }

    internal ValueTask ReleaseCatalogueHostAsync(PooledCatalogueHost host)
    {
        host.Traffic.Set(null);
        host.EntitlementHandler.Set(null);
        host.RazorpayHandler.Set(null);
        if (WasConfigurationMutated(host.Api, host.InitialSettings))
        {
            _catalogueHosts.Remove(host.Key);
            return host.Api.DisposeAsync();
        }
        return ValueTask.CompletedTask;
    }

    internal ValueTask ReleaseLicensingHostAsync(PooledLicensingHost host)
    {
        host.Traffic.Set(null);
        host.EntitlementHandler.Set(null);
        host.RazorpayHandler.Set(null);
        if (WasConfigurationMutated(host.Api, host.InitialSettings))
        {
            _licensingHosts.Remove(host.Key);
            return host.Api.DisposeAsync();
        }
        return ValueTask.CompletedTask;
    }

    private static Dictionary<string, string?> SnapshotSettings(IReadOnlyDictionary<string, string?> settings) =>
        new(settings, StringComparer.Ordinal);

    private static bool WasConfigurationMutated(LicensingApiFactory api, Dictionary<string, string?> initial)
    {
        var config = api.Services.GetRequiredService<IConfiguration>();
        foreach (var (key, expected) in initial)
        {
            if (!string.Equals(config[key], expected, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _merchantHosts.Values)
            await host.Api.DisposeAsync();
        foreach (var host in _licensingHosts.Values)
            await host.Api.DisposeAsync();
        foreach (var host in _catalogueHosts.Values)
            await host.Api.DisposeAsync();
        _merchantHosts.Clear();
        _licensingHosts.Clear();
        _catalogueHosts.Clear();
    }

    internal sealed record PooledCatalogueHost(
        string Key,
        LicensingApiFactory Api,
        SwappableLicensingDatabase Database,
        SwappableTimeProvider Clock,
        SwappableTimeProvider StatusClock,
        SwappableTimeProvider CheckoutClock,
        SwappableTimeProvider PendingLeaseClock,
        SwappableTimeProvider LeaseClock,
        SwappableAdminMailer Mailer,
        SwappableHttpMessageHandler EntitlementHandler,
        SwappableHttpMessageHandler RazorpayHandler,
        SwappableTrafficFilter Traffic,
        Dictionary<string, string?> InitialSettings);

    internal sealed record PooledLicensingHost(
        string Key,
        LicensingApiFactory Api,
        SwappableLicensingDatabase Database,
        SwappableTimeProvider Clock,
        SwappableTimeProvider StatusClock,
        SwappableTimeProvider TrialClock,
        SwappableTimeProvider CheckoutClock,
        SwappableTimeProvider PendingLeaseClock,
        SwappableTimeProvider LeaseClock,
        SwappableHttpMessageHandler EntitlementHandler,
        SwappableHttpMessageHandler RazorpayHandler,
        SwappableTrafficFilter Traffic,
        Dictionary<string, string?> InitialSettings);

    internal sealed record PooledMerchantHost(
        MerchantStartupProbeFactory Api,
        SwappableTimeProvider Clock,
        SwappableTimeProvider TrialClock,
        SwappableTimeProvider LeaseClock,
        SwappableHttpMessageHandler DelegationHandler,
        SwappableHttpMessageHandler EntitlementHandler,
        SwappableTrafficFilter Traffic);
}
