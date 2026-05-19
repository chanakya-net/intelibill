using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class ItemsControllerTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) => factory.CreateClient(new WebApplicationFactoryClientOptions
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
            password = "Pass123!Aa",
            firstName = "Test",
            lastName = "User",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}"
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
            hsnCode = "10063090",
            defaultTaxRatePercent = 5m,
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Rice", body.GetProperty("name").GetString());
        Assert.Equal(barcode, body.GetProperty("barcode").GetString());
        Assert.Equal("10063090", body.GetProperty("hsnCode").GetString());
        Assert.Equal(5m, body.GetProperty("defaultTaxRatePercent").GetDecimal());
        Assert.False(body.GetProperty("defaultTaxIncluded").GetBoolean());
    }

    [Fact]
    public async Task AddItem_WithInvalidHsnAndTax_Returns400()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/items");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            name = "Rice",
            barcode = $"ITM-{Guid.NewGuid():N}",
            description = "Premium quality",
            uom = "kg",
            isActive = true,
            hsnCode = "ABC",
            defaultTaxRatePercent = 101m,
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StreamItems_AsOwner_ReturnsNdjsonAnd200()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/items/stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson; charset=utf-8", response.Content.Headers.ContentType?.ToString());
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
            totalPurchaseCost = 500.0m,
            mrp = 100.0m,
            salesPrice = 90.0m,
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
    }

    [Fact]
    public async Task GetProductDetails_WithValidBarcode_Returns200WithDetails()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var productName = $"Product {Guid.NewGuid():N}";
        var barcode = CreateQrLikeBarcode();

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
            totalPurchaseCost = 125.0m,
            mrp = 50.0m,
            salesPrice = 45.0m,
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
    }

    [Fact]
    public async Task GetProductDetails_WhenBarcodeMissing_UsesExternalLookupAndPersistsItem()
    {
        var barcode = CreateQrLikeBarcode();
        var fakeLookup = new FakeExternalProductLookupService(
            new ExternalProductLookupResult("External Product", "External Description", null));

        using var scopedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IExternalProductLookupService>();
                services.AddSingleton<IExternalProductLookupService>(fakeLookup);
            });
        });

        using var client = CreateClient(scopedFactory);
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        using var detailsRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/items/details?barcode={Uri.EscapeDataString(barcode)}");
        detailsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var detailsResponse = await client.SendAsync(detailsRequest);

        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        var body = await detailsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("External Product", body.GetProperty("name").GetString());
        Assert.Equal("External Description", body.GetProperty("description").GetString());
        Assert.Equal("Unit", body.GetProperty("uom").GetString());
        Assert.Equal(0m, body.GetProperty("costPrice").GetDecimal());
        Assert.Equal(0m, body.GetProperty("mrp").GetDecimal());
        Assert.Equal(0m, body.GetProperty("salesPrice").GetDecimal());

        Assert.Equal(1, fakeLookup.CallCount);
        Assert.Equal($"Bearer {ownerToken}", fakeLookup.LastAuthorizationHeader);

        await using var scope = scopedFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var savedItem = await db.Items.SingleAsync(i => i.Barcode == barcode);

        Assert.Equal("External Product", savedItem.Name);
        Assert.Equal("External Description", savedItem.Description);
        Assert.Equal("Unit", savedItem.Uom);
    }

    [Fact]
    public async Task GetProductDetails_WhenExternalLookupFails_ReturnsServerError()
    {
        var barcode = CreateQrLikeBarcode();
        var fakeLookup = new FakeExternalProductLookupService(
            Error.Failure("product.lookup.failed", "External lookup failed."));

        using var scopedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IExternalProductLookupService>();
                services.AddSingleton<IExternalProductLookupService>(fakeLookup);
            });
        });

        using var client = CreateClient(scopedFactory);
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        using var detailsRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/items/details?barcode={Uri.EscapeDataString(barcode)}");
        detailsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var detailsResponse = await client.SendAsync(detailsRequest);

        Assert.Equal(HttpStatusCode.InternalServerError, detailsResponse.StatusCode);
    }

    [Fact]
    public async Task InventoryInboundBatch_WithHsnAndTax_UpdatesItemDefaultsAndPersistsBatchTax()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var productName = $"Batch Product {Guid.NewGuid():N}";
        var barcode = $"BATCH-{Guid.NewGuid():N}";

        using var inboundRequest = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound/batch");
        inboundRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        inboundRequest.Content = JsonContent.Create(new
        {
            items = new object[]
            {
                new
                {
                    clientRowId = "row-1",
                    itemName = productName,
                    barcode,
                    itemDescription = "batch row",
                    hsnCode = "0401",
                    uom = "kg",
                    batchNumber = "BATCH-ROW-001",
                    quantity = 10m,
                    totalPurchaseCost = 500m,
                    mrp = 80m,
                    salesPrice = 70m,
                    taxRatePercent = 18m,
                    taxIncluded = true,
                    expiryDate = (DateOnly?)null,
                    manufacturingDate = (DateOnly?)null,
                    supplierId = (Guid?)null,
                    referenceNumber = "REF-BATCH-1",
                    notes = "batch inbound",
                    performedAt = (DateTimeOffset?)null,
                },
            },
        });

        var inboundResponse = await client.SendAsync(inboundRequest);
        inboundResponse.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var item = await db.Items.SingleAsync(i => i.Barcode == barcode);
        var batch = await db.InventoryBatches.SingleAsync(b => b.ItemId == item.Id && b.BatchNumber == "BATCH-ROW-001");

        Assert.Equal("0401", item.HsnCode);
        Assert.Equal(18m, item.DefaultTaxRatePercent);
        Assert.True(item.DefaultTaxIncluded);
        Assert.Equal(18m, batch.TaxRatePercent);
        Assert.True(batch.TaxIncluded);
    }

    [Fact]
    public async Task InventoryInbound_WithHsnAndTax_UpdatesItemDefaultsAndPersistsBatchTax()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var productName = $"Single Product {Guid.NewGuid():N}";
        var barcode = $"SINGLE-{Guid.NewGuid():N}";

        using var inboundRequest = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound");
        inboundRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        inboundRequest.Content = JsonContent.Create(new
        {
            itemName = productName,
            barcode,
            itemDescription = "single row",
            hsnCode = "0402",
            uom = "kg",
            batchNumber = "SINGLE-ROW-001",
            quantity = 10m,
            totalPurchaseCost = 500m,
            mrp = 80m,
            salesPrice = 70m,
            taxRatePercent = 18m,
            taxIncluded = true,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            supplierId = (Guid?)null,
            referenceNumber = "REF-SINGLE-1",
            notes = "single inbound",
            performedAt = (DateTimeOffset?)null,
        });

        var inboundResponse = await client.SendAsync(inboundRequest);
        inboundResponse.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var item = await db.Items.SingleAsync(i => i.Barcode == barcode);
        var batch = await db.InventoryBatches.SingleAsync(b => b.ItemId == item.Id && b.BatchNumber == "SINGLE-ROW-001");

        Assert.Equal("0402", item.HsnCode);
        Assert.Equal(18m, item.DefaultTaxRatePercent);
        Assert.True(item.DefaultTaxIncluded);
        Assert.Equal(18m, batch.TaxRatePercent);
        Assert.True(batch.TaxIncluded);
    }

    [Fact]
    public async Task GetProductDetails_WhenBatchTaxUnavailable_UsesItemTaxDefaults()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var productName = $"Tax Fallback {Guid.NewGuid():N}";
        var barcode = $"TAX-{Guid.NewGuid():N}";

        using var createItemRequest = new HttpRequestMessage(HttpMethod.Post, "/api/items");
        createItemRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        createItemRequest.Content = JsonContent.Create(new
        {
            name = productName,
            barcode,
            description = "Fallback test product",
            uom = "kg",
            isActive = true,
            hsnCode = "0902",
            defaultTaxRatePercent = 28m,
        });
        var createItemResponse = await client.SendAsync(createItemRequest);
        createItemResponse.EnsureSuccessStatusCode();

        using var inboundRequest = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound");
        inboundRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        inboundRequest.Content = JsonContent.Create(new
        {
            itemName = productName,
            barcode,
            itemDescription = "Fallback test product",
            uom = "kg",
            batchNumber = "FALLBACK-BATCH-001",
            quantity = 4m,
            totalPurchaseCost = 80m,
            mrp = 40m,
            salesPrice = 35m,
            taxRatePercent = 5m,
            taxIncluded = true,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            supplierId = (Guid?)null,
            referenceNumber = "REF-FALLBACK",
            notes = "fallback",
            performedAt = (DateTimeOffset?)null,
        });
        var inboundResponse = await client.SendAsync(inboundRequest);
        inboundResponse.EnsureSuccessStatusCode();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var item = await db.Items.SingleAsync(i => i.Barcode == barcode);
            var batch = await db.InventoryBatches.SingleAsync(b => b.ItemId == item.Id && b.BatchNumber == "FALLBACK-BATCH-001");
            var supplier = await db.Suppliers.SingleAsync(s => s.Id == batch.SupplierId);

            item.UpdateTaxDefaults("0902", 28m, false);

            supplier.Update(
                supplier.Name,
                supplier.ContactPersonName,
                supplier.ContactPersonPhone,
                supplier.Address,
                supplier.City,
                supplier.State,
                supplier.Pin,
                isActive: false,
                supplier.IsPreferred);

            await db.SaveChangesAsync();
        }

        using var detailsRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/items/details?barcode={Uri.EscapeDataString(barcode)}");
        detailsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var detailsResponse = await client.SendAsync(detailsRequest);

        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        var body = await detailsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(28m, body.GetProperty("taxRatePercent").GetDecimal());
        Assert.False(body.GetProperty("taxIncluded").GetBoolean());
        Assert.Equal("0902", body.GetProperty("hsnCode").GetString());
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

    private sealed class FakeExternalProductLookupService(ErrorOr<ExternalProductLookupResult?> result)
        : IExternalProductLookupService
    {
        public int CallCount { get; private set; }

        public string? LastAuthorizationHeader { get; private set; }

        public Task<ErrorOr<ExternalProductLookupResult?>> LookupByBarcodeAsync(
            string barcode,
            string? authorizationHeader,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastAuthorizationHeader = authorizationHeader;
            return Task.FromResult(result);
        }
    }

    private static string CreateQrLikeBarcode() =>
        $"QR|01|{Guid.NewGuid():N}|TRACE|{Guid.NewGuid():N}|PAYLOAD|{new string('E', 24)}";
}
