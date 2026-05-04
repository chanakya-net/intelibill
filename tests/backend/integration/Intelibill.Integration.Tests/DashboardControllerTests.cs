using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class DashboardControllerTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    private static string UniqueEmail() => $"dash-{Guid.NewGuid():N}@test.com";
    private static string UniqueBarcode() => $"DASH-{Guid.NewGuid():N}";

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email = UniqueEmail(),
            password = "Pass123!",
            firstName = "Dash",
            lastName = "Tester",
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
            name = $"Dash Shop {Guid.NewGuid():N}",
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
    public async Task GetDashboard_WithoutAuth_Returns401()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/api/dashboard");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_WithoutShop_Returns400()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_WithNoData_ReturnsEmptyDashboard()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("hasNoSalesActivity").GetBoolean());
        Assert.Equal(0, body.GetProperty("salesCount").GetInt32());
        Assert.Equal(0, body.GetProperty("runningLowStockCount").GetInt32());
        Assert.Equal(0, body.GetProperty("criticalStockCount").GetInt32());
    }

    [Fact]
    public async Task GetDashboard_WithSales_ReturnsKpis()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        using var inventoryRequest = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound");
        inventoryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        inventoryRequest.Content = JsonContent.Create(new
        {
            itemName = "Dashboard Item",
            barcode,
            itemDescription = (string?)null,
            uom = "PCS",
            batchNumber = "B-001",
            quantity = 20m,
            costPrice = 50m,
            mrp = 80m,
            salesPrice = 75m,
            taxRatePercent = 5m,
            taxIncluded = false,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            supplierId = (Guid?)null,
            referenceNumber = (string?)null,
            notes = (string?)null,
            performedAt = (DateTimeOffset?)null,
        });
        var inventoryResponse = await client.SendAsync(inventoryRequest);
        inventoryResponse.EnsureSuccessStatusCode();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            customerId = (Guid?)null,
            customerName = "Dash Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 78.75m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "Dashboard Item",
                    quantity = 1m,
                    costPrice = 50m,
                    salesPrice = 75m,
                    mrp = 80m,
                    taxRatePercent = 5m,
                    isPriceIncludingTax = false,
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);

        using var dashRequest = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard");
        dashRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var dashResponse = await client.SendAsync(dashRequest);

        Assert.Equal(HttpStatusCode.OK, dashResponse.StatusCode);
        var body = await dashResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(body.GetProperty("hasNoSalesActivity").GetBoolean());
        Assert.Equal(1, body.GetProperty("salesCount").GetInt32());
    }

    [Fact]
    public async Task GetDashboard_WithDateRange_FiltersResults()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        var startDate = today.AddDays(-7);
        var endDate = today.AddDays(-1);

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/dashboard?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("hasNoSalesActivity").GetBoolean());
    }

    [Fact]
    public async Task GetDashboard_AsStaff_ReturnsLimitedData()
    {
        using var client = CreateClient();
        var ownerToken = await RegisterAsync(client);
        var ownerScopedToken = await CreateShopAsync(client, ownerToken);

        var staffEmail = UniqueEmail();
        var staffPassword = "Pass123!";
        using var addUserRequest = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        addUserRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerScopedToken);
        addUserRequest.Content = JsonContent.Create(new
        {
            shopIds = new[] { await GetShopIdFromTokenAsync(client, ownerScopedToken) },
            email = staffEmail,
            firstName = "Staff",
            lastName = "Dash",
            phoneNumber = "+919876543211",
            password = staffPassword,
            confirmPassword = staffPassword,
            role = "Staff",
        });
        var addUserResponse = await client.SendAsync(addUserRequest);
        addUserResponse.EnsureSuccessStatusCode();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/email", new
        {
            email = staffEmail,
            password = staffPassword,
        });
        loginResponse.EnsureSuccessStatusCode();
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var staffToken = loginBody.GetProperty("accessToken").GetString()!;

        using var dashRequest = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard");
        dashRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var dashResponse = await client.SendAsync(dashRequest);

        Assert.Equal(HttpStatusCode.OK, dashResponse.StatusCode);
        var body = await dashResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("hasNoSalesActivity").GetBoolean());
    }

    private static async Task<Guid> GetShopIdFromTokenAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/shops/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.EnumerateArray().First().GetProperty("shopId").GetGuid();
    }
}