using System.Net;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0101Tests
{
    [Fact]
    public async Task MerchantAndLicensing_StartIndependently()
    {
        await database.PrepareMerchantAsync();
        await VerifyMerchantStartsWithoutLicensingAsync();

        await using var licensingApi = CreateLicensingApi();
        await VerifyLicensingStartsWithoutMerchantAsync(licensingApi);
        using var licensing = licensingApi.CreateClient();
        await using var merchantApi = CreateMerchantApi();
        using var merchant = merchantApi.CreateClient();
        await VerifyRunningHostsAreIsolatedAsync(merchant, licensing);
    }

    private async Task VerifyMerchantStartsWithoutLicensingAsync()
    {
        await database.SetConnectionsAsync(LicensingPostgreSqlFixture.LicensingDatabase, allowed: false);
        try
        {
            await using var api = CreateMerchantApi();
            using var client = api.CreateClient();
            await AssertMerchantProbeAsync(client, "live", HttpStatusCode.OK, "Healthy");
            await AssertMerchantProbeAsync(client, "ready", HttpStatusCode.OK, "Degraded");
        }
        finally
        {
            await database.SetConnectionsAsync(LicensingPostgreSqlFixture.LicensingDatabase, allowed: true);
        }
    }

    private async Task VerifyLicensingStartsWithoutMerchantAsync(LicensingApiFactory api)
    {
        await database.SetConnectionsAsync(LicensingPostgreSqlFixture.MerchantDatabase, allowed: false);
        try
        {
            using var client = api.CreateClient();
            await AssertProbeAsync(client, "live", HttpStatusCode.OK, "live");
            await AssertProbeAsync(client, "ready", HttpStatusCode.OK, "ready");
        }
        finally
        {
            await database.SetConnectionsAsync(LicensingPostgreSqlFixture.MerchantDatabase, allowed: true);
        }
    }

    private async Task VerifyRunningHostsAreIsolatedAsync(HttpClient merchant, HttpClient licensing)
    {
        await AssertMerchantProbeAsync(merchant, "ready", HttpStatusCode.OK, "Degraded");
        await database.SetConnectionsAsync(LicensingPostgreSqlFixture.LicensingDatabase, allowed: false);
        try
        {
            await AssertProbeAsync(licensing, "ready", HttpStatusCode.ServiceUnavailable, "unavailable");
            await AssertMerchantProbeAsync(merchant, "ready", HttpStatusCode.OK, "Degraded");
        }
        finally
        {
            await database.SetConnectionsAsync(LicensingPostgreSqlFixture.LicensingDatabase, allowed: true);
        }

        await database.SetConnectionsAsync(LicensingPostgreSqlFixture.MerchantDatabase, allowed: false);
        try
        {
            await AssertMerchantProbeAsync(merchant, "ready", HttpStatusCode.ServiceUnavailable, "Unhealthy");
            await AssertMerchantProbeAsync(merchant, "live", HttpStatusCode.OK, "Healthy");
            await AssertProbeAsync(licensing, "ready", HttpStatusCode.OK, "ready");
        }
        finally
        {
            await database.SetConnectionsAsync(LicensingPostgreSqlFixture.MerchantDatabase, allowed: true);
        }
    }

    private MerchantStartupProbeFactory CreateMerchantApi() => new(
        database.Login(LicensingPostgreSqlFixture.MerchantDatabase, LicensingPostgreSqlFixture.MerchantRole));

    private async Task AssertMerchantProbeAsync(HttpClient client, string probe, HttpStatusCode status, string expectedBody)
    {
        using var response = await client.GetAsync(new Uri("/health/" + probe, UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"Merchant {probe}: {(int)response.StatusCode} {body}");
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(expectedBody, body);
    }
}
