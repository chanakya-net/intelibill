using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class ItemsUpdateControllerTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory _factory = new(fixture);

    public async Task InitializeAsync() => await _factory.InitializeAsync();
    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private static string UniqueEmail() => $"items-update-{Guid.NewGuid():N}@test.com";

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email = UniqueEmail(),
            password = "Pass123!",
            firstName = "Test",
            lastName = "User",
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private static async Task<string> CreateShopAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name = $"Shop {Guid.NewGuid():N}",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
        });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private static async Task<string> CreateItemAsync(HttpClient client, string token, string name, string barcode)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/items");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name,
            barcode,
            description = "Item description",
            uom = "kg",
            isActive = true,
        });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task UpdateItem_AsOwner_Returns204AndUpdatesFields()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var originalBarcode = $"ITM-{Guid.NewGuid():N}";
        var itemId = await CreateItemAsync(client, ownerToken, "Rice", originalBarcode);
        var updatedBarcode = $"ITM-{Guid.NewGuid():N}";

        using var updateRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/items/{itemId}");
        updateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        updateRequest.Content = JsonContent.Create(new
        {
            name = "Premium Rice",
            barcode = updatedBarcode,
            description = "Updated description",
            uom = "box",
        });

        var updateResponse = await client.SendAsync(updateRequest);

        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/items");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var listResponse = await client.SendAsync(listRequest);
        listResponse.EnsureSuccessStatusCode();

        var items = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var updatedItem = items.EnumerateArray().Single(i => i.GetProperty("id").GetString() == itemId);

        Assert.Equal("Premium Rice", updatedItem.GetProperty("name").GetString());
        Assert.Equal(updatedBarcode, updatedItem.GetProperty("barcode").GetString());
        Assert.Equal("Updated description", updatedItem.GetProperty("description").GetString());
        Assert.Equal("box", updatedItem.GetProperty("uom").GetString());
    }

    [Fact]
    public async Task UpdateItem_WithDuplicateBarcode_Returns409()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode1 = $"ITM-{Guid.NewGuid():N}";
        var barcode2 = $"ITM-{Guid.NewGuid():N}";

        var itemId1 = await CreateItemAsync(client, ownerToken, "Rice", barcode1);
        _ = await CreateItemAsync(client, ownerToken, "Wheat", barcode2);

        using var updateRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/items/{itemId1}");
        updateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        updateRequest.Content = JsonContent.Create(new
        {
            name = "Rice Updated",
            barcode = barcode2,
            description = "Updated",
            uom = "kg",
        });

        var updateResponse = await client.SendAsync(updateRequest);

        Assert.Equal(HttpStatusCode.Conflict, updateResponse.StatusCode);
    }
}
