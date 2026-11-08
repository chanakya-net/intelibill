using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class ExpiryPolicyTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory _factory = new(fixture);
    public async Task InitializeAsync() => await _factory.InitializeAsync();
    public Task DisposeAsync() { Dispose(); return Task.CompletedTask; }
    public void Dispose() { _factory.Dispose(); GC.SuppressFinalize(this); }

    [Fact]
    public async Task FreshShop_HasCompatibleDefaultsAndOwnerCanChangeWithReason()
    {
        using var client = await CreateShopAsync();
        using var initial = await client.GetAsync("/api/expiry-policies");
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        var policy = await initial.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, policy.GetProperty("catalogVersion").GetInt64());
        Assert.Equal("Ignore", policy.GetProperty("effectivePolicy").GetProperty("mode").GetString());
        using var updated = await client.PutAsJsonAsync("/api/expiry-policies", new
        {
            expectedCatalogVersion = 1, policy = new { mode = "Block", expiryRequired = true, sellThroughExpiryDate = true, warningDays = 7, overrideAllowed = false },
            reason = "Owner tested dates",
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var saved = await updated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, saved.GetProperty("catalogVersion").GetInt64());
        Assert.Equal("Block", saved.GetProperty("effectivePolicy").GetProperty("mode").GetString());
    }

    [Fact]
    public async Task Readiness_IsBoundedAndReportsPhysicalMissingAndExpiredLots()
    {
        using var client = await CreateShopAsync();
        foreach (var (number, expiry) in new[] { ("missing", (DateOnly?)null), ("expired", (DateOnly?)new DateOnly(2020, 1, 1)), ("future", (DateOnly?)new DateOnly(2030, 1, 1)) })
        {
            using var inbound = await client.PostAsJsonAsync("/api/inventory/inbound", new
            {
                itemName = $"Readiness {number} {Guid.NewGuid():N}", barcode = Guid.NewGuid().ToString(), uom = "pcs", batchNumber = number,
                quantity = 10m, totalPurchaseCost = 100m, mrp = 20m, salesPrice = 15m, taxRatePercent = 0m, taxIncluded = false, purchaseTaxIncluded = false,
                expiryDate = expiry,
            });
            Assert.True(inbound.IsSuccessStatusCode, await inbound.Content.ReadAsStringAsync());
        }
        using var response = await client.GetAsync("/api/expiry-policies/readiness?page=1&pageSize=1");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var results = body.GetProperty("results");
        Assert.Equal(2, results.GetProperty("totalLots").GetInt32());
        Assert.Equal(1, results.GetProperty("missingDateLots").GetInt32());
        Assert.Equal(1, results.GetProperty("expiredLots").GetInt32());
        Assert.Equal(30m, results.GetProperty("physicalQuantity").GetDecimal());
        Assert.Single(results.GetProperty("items").EnumerateArray());
        using var invalid = await client.GetAsync("/api/expiry-policies/readiness?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Theory]
    [InlineData(0, "reason", "Block", 7)]
    [InlineData(1, "", "Block", 7)]
    [InlineData(1, "reason", "Block", 366)]
    public async Task InvalidWrites_AreRefused(long expected, string reason, string mode, int warningDays)
    {
        using var client = await CreateShopAsync();
        using var response = await client.PutAsJsonAsync("/api/expiry-policies", new
        {
            expectedCatalogVersion = expected, policy = new { mode, expiryRequired = false, sellThroughExpiryDate = true, warningDays, overrideAllowed = false }, reason,
        });
        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict);
        var state = await client.GetFromJsonAsync<JsonElement>("/api/expiry-policies");
        Assert.Equal(1, state.GetProperty("catalogVersion").GetInt64());
    }

    private async Task<HttpClient> CreateShopAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var enrollment = await client.EnrollAsync(new { email = $"expiry-api-{Guid.NewGuid():N}@example.test", password = "Pass123!Aa", firstName = "Expiry", lastName = "Owner" });
        Assert.True(enrollment.IsSuccessStatusCode, await enrollment.Content.ReadAsStringAsync());
        var signed = await enrollment.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signed.GetProperty("accessToken").GetString());
        using var created = await client.PostAsJsonAsync("/api/shops", new { name = "Expiry API", address = "42 MG Road", city = "Bengaluru", state = "Karnataka", pincode = "560001" });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var shop = await created.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", shop.GetProperty("accessToken").GetString());
        return client;
    }
}
