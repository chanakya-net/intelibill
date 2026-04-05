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
        });

        var secondResponse = await client.SendAsync(secondRequest);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task GetProductDetails_WithValidProductName_Returns200WithDetails()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var productName = $"Product {Guid.NewGuid():N}";
        var barcode = $"BCODE-{Guid.NewGuid():N}";

        // Create item and batch via inventory inbound
        using var inboundRequest = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound");
        inboundRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        inboundRequest.Content = JsonContent.Create(new
        {
            itemName = productName,
            barcode,
            itemDescription = "Test Description",
            uom = "kg",
            batchNumber = "BATCH-001",
            quantity = 10.0m,
            costPrice = 50.0m,
            mrp = 100.0m,
            salesPrice = 90.0m,
            minSalePrice = 80.0m,
            taxRatePercent = 5.0m,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            referenceNumber = "REF-001",
            notes = "Test batch",
            performedAt = (DateTimeOffset?)null,
        });
        var inboundResponse = await client.SendAsync(inboundRequest);
        inboundResponse.EnsureSuccessStatusCode();

        // Get product details by name
        using var detailsRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/items/details?name={Uri.EscapeDataString(productName)}");
        detailsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var detailsResponse = await client.SendAsync(detailsRequest);

        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        var body = await detailsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Test Description", body.GetProperty("description").GetString());
        Assert.Equal("kg", body.GetProperty("uom").GetString());
        Assert.Equal(50.0m, body.GetProperty("costPrice").GetDecimal());
        Assert.Equal(100.0m, body.GetProperty("mrp").GetDecimal());
        Assert.Equal(90.0m, body.GetProperty("salesPrice").GetDecimal());
        Assert.Equal(80.0m, body.GetProperty("minSalePrice").GetDecimal());
    }

    [Fact]
    public async Task GetProductDetails_WithValidBarcode_Returns200WithDetails()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var productName = $"Product {Guid.NewGuid():N}";
        var barcode = $"BCODE-{Guid.NewGuid():N}";

        // Create item and batch
        using var inboundRequest = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound");
        inboundRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        inboundRequest.Content = JsonContent.Create(new
        {
            itemName = productName,
            barcode,
            itemDescription = "Barcode Test",
            uom = "piece",
            batchNumber = "BATCH-002",
            quantity = 5.0m,
            costPrice = 25.0m,
            mrp = 50.0m,
            salesPrice = 45.0m,
            minSalePrice = 40.0m,
            taxRatePercent = 10.0m,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            referenceNumber = "",
            notes = "",
            performedAt = (DateTimeOffset?)null,
        });
        var inboundResponse = await client.SendAsync(inboundRequest);
        inboundResponse.EnsureSuccessStatusCode();

        // Get product details by barcode
        using var detailsRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/items/details?barcode={Uri.EscapeDataString(barcode)}");
        detailsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var detailsResponse = await client.SendAsync(detailsRequest);

        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        var body = await detailsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Barcode Test", body.GetProperty("description").GetString());
        Assert.Equal("piece", body.GetProperty("uom").GetString());
        Assert.Equal(25.0m, body.GetProperty("costPrice").GetDecimal());
        Assert.Equal(50.0m, body.GetProperty("mrp").GetDecimal());
        Assert.Equal(45.0m, body.GetProperty("salesPrice").GetDecimal());
        Assert.Equal(40.0m, body.GetProperty("minSalePrice").GetDecimal());
    }

    [Fact]
    public async Task GetProductDetails_WithUnknownProduct_Returns404()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        using var detailsRequest = new HttpRequestMessage(HttpMethod.Get, "/api/items/details?name=NonExistentProduct");
        detailsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var detailsResponse = await client.SendAsync(detailsRequest);

        Assert.Equal(HttpStatusCode.NotFound, detailsResponse.StatusCode);
    }

    [Fact]
    public async Task GetProductDetails_WithoutParameters_ReturnsBadRequest()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        using var detailsRequest = new HttpRequestMessage(HttpMethod.Get, "/api/items/details");
        detailsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var detailsResponse = await client.SendAsync(detailsRequest);

        Assert.Equal(HttpStatusCode.BadRequest, detailsResponse.StatusCode);
    }

    [Fact]
    public async Task GetProductDetails_Unauthorized_Returns401()
    {
        using var client = CreateClient();

        using var detailsRequest = new HttpRequestMessage(HttpMethod.Get, "/api/items/details?name=Product");
        // No authorization header

        var detailsResponse = await client.SendAsync(detailsRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, detailsResponse.StatusCode);
    }
}
