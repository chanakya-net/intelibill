using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed partial class ShopSetupTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory _factory = new(fixture);

    public Task InitializeAsync() => _factory.InitializeAsync();
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Preferences_KeepFirstAcknowledgementAcrossDismissalAndRejectStaleWriters()
    {
        using var client = CreateClient();
        var shopId = await CreateShopAsync(client);
        var initial = await ReadAsync(await client.GetAsync($"/api/shops/{shopId}/setup/preferences"));
        Assert.Equal(0, initial.GetProperty("version").GetInt32());
        Assert.True(initial.GetProperty("showOnStartup").GetBoolean());
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("sampleReceiptAcknowledgedAt").ValueKind);

        var acknowledged = await ReadAsync(await SaveAsync(client, shopId, 0, true, true));
        Assert.Equal(1, acknowledged.GetProperty("version").GetInt32());
        var timestamp = acknowledged.GetProperty("sampleReceiptAcknowledgedAt").GetDateTimeOffset();

        using var stale = await SaveAsync(client, shopId, 0, false);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("ShopSetup.VersionConflict", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());

        var dismissed = await ReadAsync(await SaveAsync(client, shopId, 1, false, true));
        Assert.Equal(2, dismissed.GetProperty("version").GetInt32());
        Assert.False(dismissed.GetProperty("showOnStartup").GetBoolean());
        Assert.Equal(timestamp, dismissed.GetProperty("sampleReceiptAcknowledgedAt").GetDateTimeOffset());
        var repeated = await ReadAsync(await SaveAsync(client, shopId, 2, false, true));
        Assert.Equal(2, repeated.GetProperty("version").GetInt32());
        Assert.Equal(timestamp, repeated.GetProperty("sampleReceiptAcknowledgedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Preferences_LegacyShopDefaultsAndSerializesCompetingInserts()
    {
        using var client = CreateClient();
        var shopId = await CreateShopAsync(client);
        await using var admin = new NpgsqlConnection(_factory.MigratorConnectionString);
        await admin.OpenAsync();
        await using var delete = new NpgsqlCommand("DELETE FROM shop_setup_preferences WHERE shop_id = @shop", admin);
        delete.Parameters.AddWithValue("shop", shopId);
        Assert.Equal(1, await delete.ExecuteNonQueryAsync());

        var defaults = await ReadAsync(await client.GetAsync($"/api/shops/{shopId}/setup/preferences"));
        Assert.Equal(0, defaults.GetProperty("version").GetInt32());
        Assert.False(defaults.GetProperty("showOnStartup").GetBoolean());
        Assert.Equal(JsonValueKind.Null, defaults.GetProperty("sampleReceiptAcknowledgedAt").ValueKind);

        var responses = await Task.WhenAll(SaveAsync(client, shopId, 0, true), SaveAsync(client, shopId, 0, false, true));
        using var first = responses[0];
        using var second = responses[1];
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Preferences_RejectAnotherShopEvenForItsOwner()
    {
        using var client = CreateClient();
        var first = await CreateShopAsync(client);
        var second = await CreateShopAsync(client);
        using var denied = await SaveAsync(client, first, 0, false);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var untouched = await ReadAsync(await SaveAsync(client, second, 0, true));
        Assert.True(untouched.GetProperty("showOnStartup").GetBoolean());
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    private static async Task<Guid> CreateShopAsync(HttpClient client, bool declareTax = true)
    {
        if (client.DefaultRequestHeaders.Authorization is null)
        {
            var owner = await ReadAsync(await client.EnrollAsync(new
            {
                email = $"setup-{Guid.NewGuid():N}@test.com",
                password = "Pass123!Aa",
                firstName = "Setup",
                lastName = "Owner"
            }));
            SetToken(client, owner);
        }

        var shop = await ReadAsync(await client.PostAsJsonAsync("/api/shops", new
        {
            name = "Setup Shop",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
            taxProfile = declareTax ? new { taxRegime = "Unregistered", stateCode = "29", invoiceSeriesCode = "A" } : null
        }));
        SetToken(client, shop);
        return shop.GetProperty("activeShopId").GetGuid();
    }

    private static void SetToken(HttpClient client, JsonElement scope) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.GetProperty("accessToken").GetString());

    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, Guid shopId, int expectedVersion, bool showOnStartup, bool acknowledgeSample = false) =>
        client.PutAsJsonAsync($"/api/shops/{shopId}/setup/preferences", new { expectedVersion, showOnStartup, acknowledgeSample });

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
    }
}
