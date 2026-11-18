using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Integration.Tests;

public sealed partial class P0402LicensingTests
{
    [Fact]
    public async Task Local_trading_authority_reports_503_separately_from_known_expiry()
    {
        _clock.Now = EntitlementSnapshotsV1.Instant(TimeProvider.System);
        using var signer = RSA.Create(2048);
        var transport = new FixtureTransport();
        using var host = MerchantHost(signer, transport);
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var (shop, _) = await EnrollShopAsync(client);
        var binding = await BindShopAsync(shop);
        var verified = _clock.Now.AddHours(-47).AddMinutes(-59);
        var deadline = verified.AddHours(48);
        await PostSnapshotAsync(client, signer, SnapshotBody(binding, verified, deadline.AddDays(1)));

        using var contract = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "shop-trading-authority-v1.json"), Ct));
        var expected = contract.RootElement;
        var warm = await AssertAuthorityResponseAsync(client, shop, expected.GetProperty("verified"));
        Assert.Equal(shop, warm.GetProperty("shopId").GetGuid());
        Assert.Equal(verified, warm.GetProperty("authorityVerifiedAt").GetDateTimeOffset());
        Assert.Equal(deadline, warm.GetProperty("validUntil").GetDateTimeOffset());
        Assert.Equal(expected.GetProperty("verified").GetProperty("schemaVersion").GetInt32(), warm.GetProperty("schemaVersion").GetInt32());
        Assert.False(warm.GetProperty("enforced").GetBoolean());
        Assert.Equal(5, warm.EnumerateObject().Count());

        using var expiryClient = CreateClient(host);
        var (expiringShop, _) = await EnrollShopAsync(expiryClient);
        var expiringBinding = await BindShopAsync(expiringShop);
        await PostSnapshotAsync(expiryClient, signer, SnapshotBody(expiringBinding, _clock.Now, deadline));

        _clock.Now = deadline;
        await AssertAuthorityResponseAsync(client, shop, expected.GetProperty("unavailable"));
        await AssertAuthorityResponseAsync(client, Guid.NewGuid(), expected.GetProperty("foreignScope"));
        await AssertAuthorityResponseAsync(expiryClient, expiringShop, expected.GetProperty("expired"));
        expiryClient.DefaultRequestHeaders.Authorization = null;
        await AssertAuthorityResponseAsync(expiryClient, expiringShop, expected.GetProperty("invalidSession"));
        Assert.Equal(0, transport.Calls);
    }

    private static async Task<JsonElement> AssertAuthorityResponseAsync(HttpClient client, Guid shop, JsonElement expected)
    {
        using var response = await client.GetAsync($"/api/licensing/shops/{shop:D}/trading-authority", Ct);
        Assert.Equal((HttpStatusCode)expected.GetProperty("status").GetInt32(), response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        if (expected.TryGetProperty("code", out var code))
        {
            Assert.Equal(code.GetString(), body.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
            Assert.Equal(expected.GetProperty("status").GetInt32(), body.GetProperty("status").GetInt32());
        }

        return body;
    }
}
