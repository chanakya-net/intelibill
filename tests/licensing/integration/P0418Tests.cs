using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0418Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    internal const string ServiceName = "P04 Acceptance Repair";

    internal static async Task<(Merchant Shop, Guid Account)> PrepareShopAsync(P0213Fixture fixture, bool enroll = true)
    {
        var shop = await CreateTradingShopAsync(fixture.Hosts);
        var account = await P0403Tests.LinkAsync(fixture, shop);
        await DrainAsync(fixture);
        if (enroll) await P0403Tests.EnrollAsync(fixture, shop);
        return (shop, account);
    }

    internal static async Task DrainAsync(P0213Fixture fixture)
    {
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
    }

    internal static async Task<(Guid Id, string Code)> AddServiceAsync(Merchant shop)
    {
        using var response = await shop.Client.PostAsJsonAsync("/api/services", new
        {
            name = ServiceName, price = 50m, taxRatePercent = 0m, taxIncluded = false, isActive = true,
        });
        var body = await ReadAsync(response, HttpStatusCode.Created);
        return (body.GetProperty("serviceId").GetGuid(), body.GetProperty("code").GetString()!);
    }

    private static object SaleRequest(Guid serviceId, string key) => new
    {
        idempotencyKey = key, paymentMethod = "Cash", paidAmount = 50m, dueAmount = 0m,
        items = new[] { new
        {
            lineType = "Service", serviceId, barcode = "SERVICE", batchNumber = "", itemName = ServiceName,
            quantity = 1m, salesPrice = 50m, inventoryBatchId = Guid.Empty,
        } },
    };

    internal static string AuthorityPath(Guid shop) => $"/api/licensing/shops/{shop:D}/trading-authority";

    internal static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {status}, got {response.StatusCode}: {text}");
        using var json = JsonDocument.Parse(text);
        return json.RootElement.Clone();
    }

    internal static async Task ReleaseAsync(P0110CookieBrowser admin, string path, Guid id)
    {
        var releasePath = $"{path}/{id:D}/release";
        using var response = await P0212Fixture.PostAsync(admin, releasePath,
            await P0404Tests.PreviewReleaseAsync(admin, releasePath));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    internal static string EvidenceDirectory() =>
        Environment.GetEnvironmentVariable("P0418_EVIDENCE_DIR") ?? Path.GetTempPath();
}
