using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

internal sealed class P0213Fixture(P0210Fixture trial, P0213Transport transport, HttpClient merchant,
    HttpClient licensing, P0213Traffic merchantTraffic, P0213Traffic licensingTraffic) : IAsyncDisposable
{
    internal P0210Fixture Trial { get; } = trial;
    internal P0213Transport Transport { get; } = transport;
    internal P0213Traffic MerchantTraffic { get; } = merchantTraffic;
    internal P0213Traffic LicensingTraffic { get; } = licensingTraffic;
    internal Hosts Hosts => Trial.Hosts;
    internal HttpClient MerchantApi { get; } = merchant;
    internal HttpClient LicensingApi { get; } = licensing;
    internal TimeProvider AuthClock => Hosts.Fixture.Clock;
    internal NpgsqlConnectionStringBuilder MerchantDb => Hosts.Database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase);

    internal static async Task<P0213Fixture> StartAsync(LicensingPostgreSqlFixture db, bool browser = false,
        IReadOnlyDictionary<string, string?>? licensingExtras = null, Action<IServiceCollection>? licensingServices = null,
        bool? prepareProvider = null, P0201Clock? businessClock = null, bool observeDelegation = false,
        bool reopeningCutover = true, Action<IServiceCollection>? merchantServices = null, bool liveDelivery = false,
        bool countAllTraffic = false)
    {
        var transport = new P0213Transport();
        using var consent = RSA.Create(2048);
        var source = P0205Tests.MerchantSettings(consent);
        var destination = new Dictionary<string, string?>
        {
            ["ShopAffiliationDelivery:PublicKeys:local-affiliation"] = consent.ExportSubjectPublicKeyInfoPem(),
        };
        transport.AddSettings(destination, licensing: true);
        foreach (var (name, value) in licensingExtras ?? new Dictionary<string, string?>()) destination[name] = value;
        transport.AddSettings(source, licensing: false);
        var merchantTraffic = new P0213Traffic(countAllTraffic);
        var licensingTraffic = new P0213Traffic(countAllTraffic);
        var setup = new P0210HostSetup(destination, source,
            services =>
            {
                Configure(services, transport, licensingTraffic, licensing: true, liveDelivery);
                licensingServices?.Invoke(services);
            },
            services =>
            {
                Configure(services, transport, merchantTraffic, licensing: false, liveDelivery);
                merchantServices?.Invoke(services);
            },
            PrepareProvider: prepareProvider ?? !browser, UseDeliveryOnCatalogue: true, BusinessClock: businessClock);
        var trial = await P0210Fixture.StartAsync(db, publish: prepareProvider ?? !browser, browser: browser,
            transport: observeDelegation ? _ => transport.Bridge(licensing: false) : null, setup: setup);
        if (reopeningCutover)
            await EnableReopeningAsync(trial.Hosts.LicensingOwner);
        var licensing = trial.Hosts.Licensing.CreateClient();
        var merchant = trial.Hosts.Merchant.CreateClient();
        transport.Connect(trial.Hosts, merchant.BaseAddress!, licensing.BaseAddress!, browser);
        // A class fixture shares the merchant database between isolated licensing hosts.
        // Leave prior evidence in place and retire only the previous test's outstanding jobs.
        await LicensingPostgreSqlFixture.ExecuteAsync(db.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase), """
            UPDATE public.entitlement_outbox SET status = 'DeadLetter', lease_token = NULL, lease_expires_at = NULL
            WHERE status IN ('Pending', 'Leased', 'AwaitingAcknowledgement')
            """);
        return new(trial, transport, merchant, licensing, merchantTraffic, licensingTraffic);
    }

    internal static Task EnableReopeningAsync(NpgsqlConnectionStringBuilder owner) =>
        LicensingPostgreSqlFixture.ExecuteAsync(owner, "UPDATE public.reopening_publisher_cutover SET enabled = true");

    private static void Configure(IServiceCollection services, P0213Transport transport, P0213Traffic traffic, bool licensing, bool liveDelivery)
    {
        var worker = services.Single(service => service.ServiceType == typeof(IHostedService) &&
            service.ImplementationType == typeof(LeasedHttpDeliveryWorker));
        if (!liveDelivery)
        {
            services.Remove(worker);
            services.AddKeyedSingleton<TimeProvider>(LeasedHttpDeliveryPump.LeaseClockKey, transport.LeaseClock);
        }
        services.AddHttpClient(LeasedHttpDeliveryPump.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => transport.Bridge(licensing));
        services.AddSingleton<IStartupFilter>(traffic);
    }

    internal static async Task<Guid> LinkAsync(Guid account, Merchant payer, Merchant owner, Guid? term = null)
    {
        using var response = await payer.Client.PostAsJsonAsync($"/api/billing/accounts/{account:D}/affiliations", new
        {
            operationKey = Guid.NewGuid(), shopId = owner.ShopId, withoutCoverage = term is null, termId = term,
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var request = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestId").GetGuid();
        var path = $"/api/billing/accounts/{account:D}/affiliations/{owner.ShopId:D}/{request:D}/consent";
        Assert.Equal("Granted", await P0205Tests.DecideAsync(owner.Client, path, "grant"));
        Assert.Equal("Accepted", await P0205Tests.DecideAsync(owner.Client, path, "accept"));
        await P0205Tests.WaitForConsentAsync(owner.Client, path, "Applied");
        return request;
    }

    internal async Task<bool> DeliverAsync(bool source)
    {
        await using var scope = (source ? Hosts.Licensing.Services : Hosts.Merchant.Services).CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LeasedHttpDeliveryPump>().DeliverOneAsync(CancellationToken.None);
    }

    internal async Task ScanAsync(bool source)
    {
        await using var scope = (source ? Hosts.Licensing.Services : Hosts.Merchant.Services).CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<LeasedHttpDeliveryPump>().QueueDueAsync(CancellationToken.None);
    }

    internal async Task DrainAsync(bool source)
    {
        for (var attempt = 0; attempt < 20; attempt++)
            if (!await DeliverAsync(source))
                return;
        Assert.Fail("Fixture delivery queue did not drain.");
    }

    internal async Task<EntitlementSnapshotV1> LatestSnapshotAsync(Guid shop)
    {
        var body = (byte[])(await SourceScalarAsync($"""
            SELECT m.payload FROM entitlement_messages m JOIN entitlement_snapshots s USING (event_id)
            WHERE s.shop_id = '{shop:D}' ORDER BY s.shop_revision DESC LIMIT 1
            """))!;
        return JsonSerializer.Deserialize<EntitlementSnapshotV1>(body, EntitlementSnapshotsV1.Json)!;
    }

    internal Task<object?> SourceScalarAsync(string sql) =>
        LicensingPostgreSqlFixture.ReadScalarAsync(Hosts.LicensingOwner, sql);

    internal Task<object?> MerchantScalarAsync(string sql) =>
        LicensingPostgreSqlFixture.ReadScalarAsync(MerchantDb, sql);

    internal async Task<(HttpResponseMessage Http, EntitlementAcknowledgementV1? Acknowledgement)> SendAsync(
        EntitlementSnapshotV1 snapshot)
    {
        using var request = await Transport.SignedAsync(snapshot, AuthClock);
        var http = await MerchantApi.SendAsync(request);
        var ack = http.IsSuccessStatusCode
            ? await http.Content.ReadFromJsonAsync<EntitlementAcknowledgementV1>(EntitlementSnapshotsV1.Json) : null;
        return (http, ack);
    }

    public async ValueTask DisposeAsync()
    {
        MerchantApi.Dispose();
        LicensingApi.Dispose();
        await Trial.DisposeAsync();
        Transport.Dispose();
    }
}
