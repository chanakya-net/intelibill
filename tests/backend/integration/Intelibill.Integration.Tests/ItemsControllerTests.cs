using System.Net;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
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
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"AddItem failed ({(int)response.StatusCode}): {errorBody}");
        }
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private static async Task<(Guid ShopId, string Token)> CreateShopAsyncWithId(HttpClient client, string token)
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
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"AddItem failed ({(int)response.StatusCode}): {errorBody}");
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("activeShopId").GetGuid(), body.GetProperty("accessToken").GetString()!);
    }

    private static async Task<(Guid UserId, string Email, string Password)> AddUserAsync(
        HttpClient client,
        string ownerToken,
        Guid shopId,
        string role)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            shopIds = new[] { shopId },
            email = UniqueEmail(),
            firstName = "Shop",
            lastName = role,
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}",
            password = "Pass123!Aa",
            confirmPassword = "Pass123!Aa",
            role,
        });

        var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"AddUser failed ({(int)response.StatusCode}): {errorBody}");
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (
            body.GetProperty("userId").GetGuid(),
            body.GetProperty("email").GetString()!,
            "Pass123!Aa");
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login/email", new
        {
            email,
            password,
        });
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
    public async Task GenerateItemBarcode_AsOwner_Returns200WithShopScopedCode()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/items/barcodes/generate");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var barcode = body.GetProperty("barcode").GetString()!;

        Assert.Matches(@"^IB-\d{6}$", barcode);

        using var scopedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/items/barcodes/generate");
        scopedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var secondResponse = await client.SendAsync(scopedRequest);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        var secondBarcode = secondBody.GetProperty("barcode").GetString()!;
        Assert.Equal("IB-000002", secondBarcode);
    }

    [Fact]
    public async Task GenerateItemBarcode_AsManager_Returns200()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (shopId, ownerToken) = await CreateShopAsyncWithId(client, token);
        var (_, email, password) = await AddUserAsync(client, ownerToken, shopId, "Manager");
        var managerToken = await LoginAsync(client, email, password);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/items/barcodes/generate");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("IB-000001", body.GetProperty("barcode").GetString());
    }

    [Fact]
    public async Task GenerateItemBarcode_AsStaff_ReturnsForbidden()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (shopId, ownerToken) = await CreateShopAsyncWithId(client, token);
        var (_, staffEmail, staffPassword) = await AddUserAsync(client, ownerToken, shopId, "Staff");
        var staffToken = await LoginAsync(client, staffEmail, staffPassword);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/items/barcodes/generate");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GenerateItemBarcode_IsShopScopedAcrossShops()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerTokenShopA) = await CreateShopAsyncWithId(client, token);
        var (_, ownerTokenShopB) = await CreateShopAsyncWithId(client, token);

        using var requestShopA = new HttpRequestMessage(HttpMethod.Post, "/api/items/barcodes/generate");
        requestShopA.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenShopA);
        var responseShopA = await client.SendAsync(requestShopA);
        Assert.Equal(HttpStatusCode.OK, responseShopA.StatusCode);
        var bodyShopA = await responseShopA.Content.ReadFromJsonAsync<JsonElement>();
        var barcodeShopA = bodyShopA.GetProperty("barcode").GetString()!;

        using var requestShopB = new HttpRequestMessage(HttpMethod.Post, "/api/items/barcodes/generate");
        requestShopB.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenShopB);
        var responseShopB = await client.SendAsync(requestShopB);
        Assert.Equal(HttpStatusCode.OK, responseShopB.StatusCode);
        var bodyShopB = await responseShopB.Content.ReadFromJsonAsync<JsonElement>();
        var barcodeShopB = bodyShopB.GetProperty("barcode").GetString()!;

        Assert.Equal("IB-000001", barcodeShopA);
        Assert.Equal("IB-000001", barcodeShopB);
    }

    [Fact]
    public async Task GenerateItemBarcode_ConcurrentRequests_ForSameShopReturnSequentialUniqueCodes()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcodeTasks = Enumerable
            .Range(0, 8)
            .Select(_ => GenerateItemBarcodeAsync(client, ownerToken))
            .ToArray();

        var barcodes = await Task.WhenAll(barcodeTasks);

        Assert.Equal(8, barcodes.Length);
        Assert.All(barcodes, barcode => Assert.Matches(@"^IB-\d{6}$", barcode));
        Assert.Equal(8, barcodes.Distinct().Count());

        var sequenceNumbers = barcodes
            .Select(barcode => int.Parse(barcode["IB-".Length..], CultureInfo.InvariantCulture))
            .OrderBy(value => value)
            .ToArray();

        Assert.Equal(8, sequenceNumbers.Length);
        for (var i = 0; i < sequenceNumbers.Length; i++)
        {
            Assert.Equal(i + 1, sequenceNumbers[i]);
        }
    }

    [Fact]
    public async Task PrintBarcodeLabels_AsOwner_ReturnsPdf()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var itemId = await AddItemAsync(client, ownerToken, $"Label Item {Guid.NewGuid():N}", $"LBL-{Guid.NewGuid():N}", isActive: true);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var item = await db.Items.SingleAsync(x => x.Id == itemId);
            await SeedInventoryAsync(
                db,
                item.ShopId,
                item.Id,
                item.CreatedBy,
                quantity: 5m,
                reorderLevel: 1m,
                new CatalogBatchSeed("LBL-B1", 5m, 75m, 100m, 90m, DateTimeOffset.UtcNow));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/items/barcodes/labels");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            items = new[]
            {
                new
                {
                    itemId,
                    quantity = 2,
                    inventoryBatchId = (Guid?)null,
                },
            },
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var content = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(content);
        Assert.True(content.AsSpan(0, Math.Min(content.Length, 5)).SequenceEqual("%PDF-"u8));
    }

    [Fact]
    public async Task PrintBarcodeLabels_AsStaff_ReturnsForbidden()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (shopId, ownerToken) = await CreateShopAsyncWithId(client, token);
        var (_, staffEmail, staffPassword) = await AddUserAsync(client, ownerToken, shopId, "Staff");
        var staffToken = await LoginAsync(client, staffEmail, staffPassword);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/items/barcodes/labels");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        request.Content = JsonContent.Create(new
        {
            items = new[]
            {
                new
                {
                    itemId = Guid.NewGuid(),
                    quantity = 1,
                    inventoryBatchId = (Guid?)null,
                },
            },
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PrintBarcodeLabels_WhenItemFromAnotherShop_ReturnsBadRequest()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerTokenShopA) = await CreateShopAsyncWithId(client, token);
        var (_, ownerTokenShopB) = await CreateShopAsyncWithId(client, token);
        var itemIdFromShopA = await AddItemAsync(
            client,
            ownerTokenShopA,
            $"Cross Shop Item {Guid.NewGuid():N}",
            $"XSHOP-{Guid.NewGuid():N}",
            isActive: true);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/items/barcodes/labels");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenShopB);
        request.Content = JsonContent.Create(new
        {
            items = new[]
            {
                new
                {
                    itemId = itemIdFromShopA,
                    quantity = 1,
                    inventoryBatchId = (Guid?)null,
                },
            },
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Item.BarcodeLabelItemNotFound", body.GetProperty("title").GetString());
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

    [Fact]
    public async Task GetItems_PaginatesAndReturnsShopSummaryAcrossAllFilteredItems()
    {
        using var client = CreateClient();
        var fixture = await SeedCatalogAsync(client);

        using var firstPageRequest = new HttpRequestMessage(HttpMethod.Get, "/api/items?pageNumber=1&pageSize=2");
        firstPageRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.OwnerToken);

        var firstPageResponse = await client.SendAsync(firstPageRequest);
        Assert.Equal(HttpStatusCode.OK, firstPageResponse.StatusCode);

        var firstPage = await firstPageResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, firstPage.GetProperty("pageNumber").GetInt32());
        Assert.Equal(2, firstPage.GetProperty("pageSize").GetInt32());
        Assert.Equal(4, firstPage.GetProperty("totalCount").GetInt32());

        var firstPageItems = firstPage.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, firstPageItems.Length);
        Assert.Equal(fixture.InStockName, firstPageItems[0].GetProperty("name").GetString());
        Assert.Equal("inStock", firstPageItems[0].GetProperty("stockStatus").GetString());
        Assert.Equal(13m, firstPageItems[0].GetProperty("unitPrice").GetDecimal());
        Assert.Equal(122m, firstPageItems[0].GetProperty("currentStockValue").GetDecimal());

        Assert.Equal(fixture.ReorderName, firstPageItems[1].GetProperty("name").GetString());
        Assert.Equal("runningLow", firstPageItems[1].GetProperty("stockStatus").GetString());
        Assert.Equal(20m, firstPageItems[1].GetProperty("unitPrice").GetDecimal());
        Assert.Equal(40m, firstPageItems[1].GetProperty("currentStockValue").GetDecimal());

        var summary = firstPage.GetProperty("summary");
        Assert.Equal(4, summary.GetProperty("totalItems").GetInt32());
        Assert.Equal(3, summary.GetProperty("activeItems").GetInt32());
        Assert.Equal(1, summary.GetProperty("inactiveItems").GetInt32());
        Assert.Equal(1, summary.GetProperty("runningLowStockCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("criticalStockCount").GetInt32());
        Assert.Equal(162m, summary.GetProperty("totalStockValue").GetDecimal());

        using var secondPageRequest = new HttpRequestMessage(HttpMethod.Get, "/api/items?pageNumber=2&pageSize=2");
        secondPageRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.OwnerToken);

        var secondPageResponse = await client.SendAsync(secondPageRequest);
        Assert.Equal(HttpStatusCode.OK, secondPageResponse.StatusCode);

        var secondPage = await secondPageResponse.Content.ReadFromJsonAsync<JsonElement>();
        var secondPageItems = secondPage.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, secondPageItems.Length);
        Assert.Equal(fixture.OutOfStockName, secondPageItems[0].GetProperty("name").GetString());
        Assert.Equal("critical", secondPageItems[0].GetProperty("stockStatus").GetString());
        Assert.Null(ReadDecimalOrNull(secondPageItems[0].GetProperty("unitPrice")));

        Assert.Equal(fixture.InactiveName, secondPageItems[1].GetProperty("name").GetString());
        Assert.Equal("inactive", secondPageItems[1].GetProperty("stockStatus").GetString());
        Assert.Null(ReadDecimalOrNull(secondPageItems[1].GetProperty("unitPrice")));
    }

    [Fact]
    public async Task GetItems_NormalizesInvalidPaginationValues()
    {
        using var client = CreateClient();
        var fixture = await SeedCatalogAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/items?pageNumber=0&pageSize=0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.OwnerToken);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("pageNumber").GetInt32());
        Assert.Equal(20, body.GetProperty("pageSize").GetInt32());
        Assert.Equal(4, body.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task GetItems_CapsPageSizeAt100()
    {
        using var client = CreateClient();
        var fixture = await SeedCatalogAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/items?pageSize=999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.OwnerToken);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(100, body.GetProperty("pageSize").GetInt32());
        Assert.Equal(4, body.GetProperty("items").GetArrayLength());
    }

    [Fact]
	    public async Task GetItems_SearchAndStatusFiltersOperateServerSide()
	    {
	        using var client = CreateClient();
	        var fixture = await SeedCatalogAsync(client);
	        const int expectedTotalItems = 4;
	        const int expectedActiveItems = 3;
	        const int expectedInactiveItems = 1;
	        const int expectedRunningLowStock = 1;
	        const int expectedCriticalStock = 1;
	        const decimal expectedTotalStockValue = 162m;

	        using var nameSearchRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/items?search={Uri.EscapeDataString("brav")}");
	        nameSearchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.OwnerToken);

	        var nameSearchResponse = await client.SendAsync(nameSearchRequest);
	        Assert.Equal(HttpStatusCode.OK, nameSearchResponse.StatusCode);
	        var nameSearch = await nameSearchResponse.Content.ReadFromJsonAsync<JsonElement>();
	        Assert.Equal(1, nameSearch.GetProperty("totalCount").GetInt32());
	        Assert.Equal(fixture.ReorderName, nameSearch.GetProperty("items").EnumerateArray().Single().GetProperty("name").GetString());
	        var nameSearchSummary = nameSearch.GetProperty("summary");
	        Assert.Equal(expectedTotalItems, nameSearchSummary.GetProperty("totalItems").GetInt32());
	        Assert.Equal(expectedActiveItems, nameSearchSummary.GetProperty("activeItems").GetInt32());
	        Assert.Equal(expectedInactiveItems, nameSearchSummary.GetProperty("inactiveItems").GetInt32());
	        Assert.Equal(expectedRunningLowStock, nameSearchSummary.GetProperty("runningLowStockCount").GetInt32());
	        Assert.Equal(expectedCriticalStock, nameSearchSummary.GetProperty("criticalStockCount").GetInt32());
	        Assert.Equal(expectedTotalStockValue, nameSearchSummary.GetProperty("totalStockValue").GetDecimal());

	        using var barcodeSearchRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/items?search={Uri.EscapeDataString(fixture.OutOfStockBarcode)}");
	        barcodeSearchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.OwnerToken);

	        var barcodeSearchResponse = await client.SendAsync(barcodeSearchRequest);
	        Assert.Equal(HttpStatusCode.OK, barcodeSearchResponse.StatusCode);
	        var barcodeSearch = await barcodeSearchResponse.Content.ReadFromJsonAsync<JsonElement>();
	        Assert.Equal(1, barcodeSearch.GetProperty("totalCount").GetInt32());
	        Assert.Equal(fixture.OutOfStockName, barcodeSearch.GetProperty("items").EnumerateArray().Single().GetProperty("name").GetString());
	        var barcodeSearchSummary = barcodeSearch.GetProperty("summary");
	        Assert.Equal(expectedTotalItems, barcodeSearchSummary.GetProperty("totalItems").GetInt32());
	        Assert.Equal(expectedActiveItems, barcodeSearchSummary.GetProperty("activeItems").GetInt32());
	        Assert.Equal(expectedInactiveItems, barcodeSearchSummary.GetProperty("inactiveItems").GetInt32());
	        Assert.Equal(expectedRunningLowStock, barcodeSearchSummary.GetProperty("runningLowStockCount").GetInt32());
	        Assert.Equal(expectedCriticalStock, barcodeSearchSummary.GetProperty("criticalStockCount").GetInt32());
	        Assert.Equal(expectedTotalStockValue, barcodeSearchSummary.GetProperty("totalStockValue").GetDecimal());

	        using var descriptionSearchRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/items?search={Uri.EscapeDataString(fixture.InStockDescription)}");
	        descriptionSearchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.OwnerToken);

	        var descriptionSearchResponse = await client.SendAsync(descriptionSearchRequest);
	        Assert.Equal(HttpStatusCode.OK, descriptionSearchResponse.StatusCode);
	        var descriptionSearch = await descriptionSearchResponse.Content.ReadFromJsonAsync<JsonElement>();
	        Assert.Equal(1, descriptionSearch.GetProperty("totalCount").GetInt32());
	        Assert.Equal(fixture.InStockName, descriptionSearch.GetProperty("items").EnumerateArray().Single().GetProperty("name").GetString());

	        using var uomSearchRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/items?search={Uri.EscapeDataString(fixture.ReorderUom)}");
	        uomSearchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.OwnerToken);

	        var uomSearchResponse = await client.SendAsync(uomSearchRequest);
	        Assert.Equal(HttpStatusCode.OK, uomSearchResponse.StatusCode);
	        var uomSearch = await uomSearchResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, uomSearch.GetProperty("totalCount").GetInt32());
        Assert.Equal(fixture.ReorderName, uomSearch.GetProperty("items").EnumerateArray().Single().GetProperty("name").GetString());

	        using var hsnCodeSearchRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/items?search={Uri.EscapeDataString(fixture.OutOfStockHsnCode)}");
	        hsnCodeSearchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.OwnerToken);

	        var hsnCodeSearchResponse = await client.SendAsync(hsnCodeSearchRequest);
	        Assert.Equal(HttpStatusCode.OK, hsnCodeSearchResponse.StatusCode);
	        var hsnCodeSearch = await hsnCodeSearchResponse.Content.ReadFromJsonAsync<JsonElement>();
	        Assert.Equal(1, hsnCodeSearch.GetProperty("totalCount").GetInt32());
	        Assert.Equal(fixture.OutOfStockName, hsnCodeSearch.GetProperty("items").EnumerateArray().Single().GetProperty("name").GetString());

        foreach (var (status, expectedCount, expectedName) in new[]
        {
            ("active", 3, fixture.InStockName),
            ("inactive", 1, fixture.InactiveName),
            ("inStock", 1, fixture.InStockName),
            ("runningLow", 1, fixture.ReorderName),
            ("critical", 1, fixture.OutOfStockName),
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/items?status={Uri.EscapeDataString(status)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.OwnerToken);

            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

	            Assert.Equal(expectedCount, body.GetProperty("totalCount").GetInt32());
	            Assert.Equal(expectedCount, body.GetProperty("items").GetArrayLength());
	            Assert.Contains(body.GetProperty("items").EnumerateArray(), item => item.GetProperty("name").GetString() == expectedName);
	            var summary = body.GetProperty("summary");
	            Assert.Equal(expectedTotalItems, summary.GetProperty("totalItems").GetInt32());
	            Assert.Equal(expectedActiveItems, summary.GetProperty("activeItems").GetInt32());
	            Assert.Equal(expectedInactiveItems, summary.GetProperty("inactiveItems").GetInt32());
	            Assert.Equal(expectedRunningLowStock, summary.GetProperty("runningLowStockCount").GetInt32());
	            Assert.Equal(expectedCriticalStock, summary.GetProperty("criticalStockCount").GetInt32());
	            Assert.Equal(expectedTotalStockValue, summary.GetProperty("totalStockValue").GetDecimal());
	        }
	    }

    [Fact]
    public async Task GetItems_UsesLatestNonVoidedBatchPriceAndSumsAllBatchValue()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var name = $"Multi Batch {Guid.NewGuid():N}";
        var barcode = $"MB-{Guid.NewGuid():N}";

        var itemId = await AddItemAsync(client, ownerToken, name, barcode, isActive: true);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbItem = await db.Items.SingleAsync(i => i.Id == itemId);
            await SeedInventoryAsync(
                db,
                dbItem.ShopId,
                dbItem.Id,
                dbItem.CreatedBy,
                quantity: 10m,
                reorderLevel: 5m,
                new CatalogBatchSeed(
                    "B-OLD",
                    4m,
                    1m,
                    15m,
                    11m,
                    new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt: new DateTimeOffset(2026, 5, 3, 0, 0, 0, TimeSpan.Zero)),
                new CatalogBatchSeed(
                    "B-NEW",
                    6m,
                    2m,
                    18m,
                    13m,
                    new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt: new DateTimeOffset(2026, 5, 2, 1, 0, 0, TimeSpan.Zero)));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/items?search={Uri.EscapeDataString(name)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var resultItem = body.GetProperty("items").EnumerateArray().Single();

        Assert.Equal(13m, resultItem.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(122m, resultItem.GetProperty("currentStockValue").GetDecimal());
        Assert.Equal("inStock", resultItem.GetProperty("stockStatus").GetString());
        Assert.Equal(122m, body.GetProperty("summary").GetProperty("totalStockValue").GetDecimal());
    }

    private static async Task<Guid> AddItemAsync(
        HttpClient client,
        string token,
        string name,
        string barcode,
        bool isActive,
        string? description = null,
        string uom = "kg",
        string? hsnCode = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/items");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name,
            barcode,
            description,
            uom,
            isActive,
            hsnCode,
            defaultTaxRatePercent = 0m,
        });

        var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"AddItem failed ({(int)response.StatusCode}): {errorBody}");
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private static async Task<string> GenerateItemBarcodeAsync(HttpClient client, string ownerToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/items/barcodes/generate");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("barcode").GetString()!;
    }

    private static async Task SeedInventoryAsync(
        ApplicationDbContext db,
        Guid shopId,
        Guid itemId,
        Guid createdBy,
        decimal quantity,
        decimal reorderLevel,
        params CatalogBatchSeed[] batches)
    {
        var inventory = Inventory.Create(shopId, itemId, quantity, reorderLevel, Math.Max(quantity, reorderLevel), createdBy).Value;
        await db.Inventory.AddAsync(inventory);

        foreach (var batchSeed in batches)
        {
            var batch = InventoryBatch.Create(
                shopId,
                itemId,
                batchSeed.BatchNumber,
                batchSeed.Quantity,
                batchSeed.CostPrice,
                batchSeed.Mrp,
                batchSeed.SalesPrice,
                0m,
                false,
                null,
                null,
                null,
                createdBy).Value;

            SetTimestamps(batch, batchSeed.CreatedAt, batchSeed.UpdatedAt);
            await db.InventoryBatches.AddAsync(batch);
        }

        await db.SaveChangesAsync();
    }

    private static void SetTimestamps(BaseEntity entity, DateTimeOffset createdAt, DateTimeOffset? updatedAt = null)
    {
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.CreatedAt))!.SetValue(entity, createdAt);
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.UpdatedAt))!.SetValue(entity, updatedAt);
    }

    private async Task<CatalogSeedData> SeedCatalogAsync(HttpClient client)
    {
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var inStockName = $"Alpha In Stock {Guid.NewGuid():N}";
        var inStockBarcode = $"CAT-1-{Guid.NewGuid():N}";
        var reorderName = $"Bravo Reorder {Guid.NewGuid():N}";
        var reorderBarcode = $"CAT-2-{Guid.NewGuid():N}";
        var outOfStockName = $"Charlie Out {Guid.NewGuid():N}";
        var outOfStockBarcode = $"CAT-3-{Guid.NewGuid():N}";
        var inactiveName = $"Delta Inactive {Guid.NewGuid():N}";
        var inactiveBarcode = $"CAT-4-{Guid.NewGuid():N}";

        var inStockDescription = $"premium alpha desc {Guid.NewGuid():N}";
        var reorderUom = "bx";
        var outOfStockHsnCode = "1234";

        var inStockItemId = await AddItemAsync(client, ownerToken, inStockName, inStockBarcode, isActive: true, description: inStockDescription, uom: "kg");
        var reorderItemId = await AddItemAsync(client, ownerToken, reorderName, reorderBarcode, isActive: true, uom: reorderUom);
        await AddItemAsync(client, ownerToken, outOfStockName, outOfStockBarcode, isActive: true, hsnCode: outOfStockHsnCode);
        await AddItemAsync(client, ownerToken, inactiveName, inactiveBarcode, isActive: false);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var inStockItem = await db.Items.SingleAsync(item => item.Id == inStockItemId);
            var reorderItem = await db.Items.SingleAsync(item => item.Id == reorderItemId);

            await SeedInventoryAsync(
                db,
                inStockItem.ShopId,
                inStockItem.Id,
                inStockItem.CreatedBy,
                quantity: 10m,
                reorderLevel: 5m,
                new CatalogBatchSeed("B-OLD", 4m, 1m, 15m, 11m, new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)),
                new CatalogBatchSeed("B-NEW", 6m, 2m, 18m, 13m, new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero)));

            await SeedInventoryAsync(
                db,
                reorderItem.ShopId,
                reorderItem.Id,
                reorderItem.CreatedBy,
                quantity: 2m,
                reorderLevel: 5m,
                new CatalogBatchSeed("B-REORDER", 2m, 1m, 25m, 20m, new DateTimeOffset(2026, 5, 3, 0, 0, 0, TimeSpan.Zero)));
        }

        return new CatalogSeedData(
            ownerToken,
            inStockName,
            inStockBarcode,
            inStockDescription,
            reorderName,
            reorderBarcode,
            reorderUom,
            outOfStockName,
            outOfStockBarcode,
            outOfStockHsnCode,
            inactiveName,
            inactiveBarcode);
    }

    private static decimal? ReadDecimalOrNull(JsonElement element) =>
        element.ValueKind == JsonValueKind.Null ? null : element.GetDecimal();

    private sealed record CatalogSeedData(
        string OwnerToken,
        string InStockName,
        string InStockBarcode,
        string InStockDescription,
        string ReorderName,
        string ReorderBarcode,
        string ReorderUom,
        string OutOfStockName,
        string OutOfStockBarcode,
        string OutOfStockHsnCode,
        string InactiveName,
        string InactiveBarcode);

    private sealed record CatalogBatchSeed(
        string BatchNumber,
        decimal Quantity,
        decimal CostPrice,
        decimal Mrp,
        decimal SalesPrice,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt = null);

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
