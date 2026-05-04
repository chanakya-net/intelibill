using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class ProfitLossControllerTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    private static string UniqueEmail() => $"pl-{Guid.NewGuid():N}@test.com";
    private static string UniqueBarcode() => $"PL-{Guid.NewGuid():N}";

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email = UniqueEmail(),
            password = "Pass123!",
            firstName = "PL",
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
            name = "PL Shop",
            address = "PL Street",
            city = "City",
            state = "State",
            pincode = "560001",
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private static async Task SetupInventoryAsync(HttpClient client, string token, string barcode)
    {
        using var supplierRequest = new HttpRequestMessage(HttpMethod.Post, "/api/suppliers");
        supplierRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        supplierRequest.Content = JsonContent.Create(new
        {
            name = "PL Supplier",
            contactPerson = "PL Contact",
            contactPhone = "+919876543211",
            address = "Supplier Street",
            city = "City",
            state = "State",
            pin = "560002",
            isPreferred = true,
        });
        var supplierResponse = await client.SendAsync(supplierRequest);
        supplierResponse.EnsureSuccessStatusCode();
        var supplierBody = await supplierResponse.Content.ReadFromJsonAsync<JsonElement>();
        var supplierId = supplierBody.GetProperty("supplierId").GetGuid();

        using var itemRequest = new HttpRequestMessage(HttpMethod.Post, "/api/items");
        itemRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        itemRequest.Content = JsonContent.Create(new
        {
            name = "PL Item",
            barcode = barcode,
            uom = "PCS",
            supplierId = supplierId,
        });
        var itemResponse = await client.SendAsync(itemRequest);
        itemResponse.EnsureSuccessStatusCode();

        using var inventoryRequest = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound");
        inventoryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        inventoryRequest.Content = JsonContent.Create(new
        {
            itemName = "PL Item",
            barcode = barcode,
            itemDescription = "Test Item",
            uom = "PCS",
            batchNumber = "B-001",
            quantity = 10m,
            costPrice = 80m,
            salesPrice = 100m,
            mrp = 120m,
            taxRatePercent = 18m,
            taxIncluded = false,
            expiryDate = (string?)null,
            manufacturingDate = (string?)null,
            supplierId = supplierId,
            referenceNumber = "REF-001",
            notes = "Test Note",
            performedAt = DateTimeOffset.UtcNow,
        });
        var inventoryResponse = await client.SendAsync(inventoryRequest);
        inventoryResponse.EnsureSuccessStatusCode();
    }

    // ======================= EXISTING TEST (PRESERVED) =======================

    [Fact]
    public async Task GetProfitLossReport_ReturnsCorrectData()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        await SetupInventoryAsync(client, ownerToken, barcode);

        // Item: 80 cost, 100 sales, 18% tax -> 118 total. 1 unit.
        // Revenue Before Tax: 100
        // Revenue After Tax: 118
        // Cost: 80
        // Profit Before Tax: 118 - 80 = 38
        // Profit After Tax: 100 - 80 = 20

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            customerId = (Guid?)null,
            customerName = "PL Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 118m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "PL Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);

        using var reportRequest = new HttpRequestMessage(HttpMethod.Get, "/api/sales/profit-loss");
        reportRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var reportResponse = await client.SendAsync(reportRequest);

        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);
        var report = await reportResponse.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.NotEmpty(report!);
        var item = report![0];
        
        Assert.Equal(80m, item.GetProperty("totalCost").GetDecimal());
        Assert.Equal(100m, item.GetProperty("revenueBeforeTax").GetDecimal());
        Assert.Equal(118m, item.GetProperty("revenueAfterTax").GetDecimal());
        Assert.Equal(38m, item.GetProperty("profitBeforeTax").GetDecimal());
        Assert.Equal(20m, item.GetProperty("profitAfterTax").GetDecimal());
    }

    // ======================= NEW: NEGATIVE CASES =======================

    [Fact]
    public async Task GetProfitLossReport_WithNoSales_ReturnsEmpty()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales/profit-loss");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.Empty(body!);
    }

    [Fact]
    public async Task GetProfitLossReport_WithoutAuth_Returns401()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/api/sales/profit-loss");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProfitLossReport_WithoutShop_Returns400()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales/profit-loss");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetProfitLossReport_OtherShopIsolation_ReturnsEmpty()
    {
        using var client = CreateClient();
        var tokenA = await RegisterAsync(client);
        var ownerTokenA = await CreateShopAsync(client, tokenA);

        var barcode = UniqueBarcode();
        await SetupInventoryAsync(client, ownerTokenA, barcode);

        var tokenB = await RegisterAsync(client);
        var ownerTokenB = await CreateShopAsync(client, tokenB);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales/profit-loss");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.Empty(body!);
    }
}