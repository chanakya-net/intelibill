using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelibill.Integration.Tests;

public class ItemsControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public ItemsControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private static string UniqueEmail() => $"items-{Guid.NewGuid():N}@test.com";

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

    [Fact]
    public async Task AddItem_AsOwner_Returns200()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var barcode = $"ITM-{Guid.NewGuid():N}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/items");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            name = "Rice",
            barcode,
            description = "Premium quality",
            uom = "kg",
            isActive = true,
            preferredSupplierId = (Guid?)null,
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Rice", body.GetProperty("name").GetString());
        Assert.Equal(barcode, body.GetProperty("barcode").GetString());
    }

    [Fact]
    public async Task AddItem_WithDuplicateBarcode_Returns409()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var barcode = $"ITM-{Guid.NewGuid():N}";

        using var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/items");
        firstRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        firstRequest.Content = JsonContent.Create(new
        {
            name = "Rice",
            barcode,
            description = "Premium quality",
            uom = "kg",
            isActive = true,
            preferredSupplierId = (Guid?)null,
        });
        var firstResponse = await client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        using var secondRequest = new HttpRequestMessage(HttpMethod.Post, "/api/items");
        secondRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        secondRequest.Content = JsonContent.Create(new
        {
            name = "Rice 2",
            barcode,
            description = "Another",
            uom = "kg",
            isActive = true,
            preferredSupplierId = (Guid?)null,
        });

        var secondResponse = await client.SendAsync(secondRequest);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }
}
