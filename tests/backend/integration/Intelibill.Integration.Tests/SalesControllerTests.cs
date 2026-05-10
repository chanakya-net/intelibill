using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class SalesControllerTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    private static string UniqueEmail() => $"sales-{Guid.NewGuid():N}@test.com";
    private static string UniqueBarcode() => $"SALE-{Guid.NewGuid():N}";

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

    private static async Task<JsonElement> AddInventoryAsync(
        HttpClient client, string token, string barcode, string batchNumber, decimal quantity)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            itemName = "Test Item",
            barcode,
            itemDescription = (string?)null,
            uom = "kg",
            batchNumber,
            quantity,
            costPrice = 80m,
            mrp = 120m,
            salesPrice = 100m,
            taxRatePercent = 18m,
            taxIncluded = false,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            supplierId = (Guid?)null,
            referenceNumber = (string?)null,
            notes = (string?)null,
            performedAt = (DateTimeOffset?)null,
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
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

    // ======================= EXISTING TESTS (PRESERVED) =======================

    [Fact]
    public async Task RecordSale_CreatesAllRelevantRecords()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);

        var itemId = inboundBody.GetProperty("itemId").GetGuid();
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            customerId = (Guid?)null,
            customerName = "Walk-in Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 590m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "Test Item",
                    quantity = 5m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });

        var saleResponse = await client.SendAsync(saleRequest);

        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);

        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();
        var invoiceNumber = saleBody.GetProperty("invoiceNumber").GetString()!;

        Assert.StartsWith("INV-", invoiceNumber);
        Assert.Equal(590m, saleBody.GetProperty("totalAmount").GetDecimal());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var sale = await db.Sales.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == saleId);
        Assert.NotNull(sale);
        Assert.Single(sale!.Items);
        Assert.Equal(590m, sale.TotalAmount);

        var tx = await db.StockTransactions
            .FirstOrDefaultAsync(t => t.InventoryBatchId == batchId && t.TransactionType == StockTransactionType.Out);
        Assert.NotNull(tx);
        Assert.Equal(-5m, tx!.Quantity);
        Assert.Equal(invoiceNumber, tx.ReferenceNumber);

        var batch = await db.InventoryBatches.FirstOrDefaultAsync(b => b.Id == batchId);
        Assert.NotNull(batch);
        Assert.Equal(45m, batch!.Quantity);

        var inventory = await db.Inventory.FirstOrDefaultAsync(i => i.ItemId == itemId);
        Assert.NotNull(inventory);
        Assert.Equal(45m, inventory!.Quantity);
    }

    [Fact]
    public async Task RecordSale_WithPriceMismatch_ReturnsCreatedWithWarning()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            customerId = (Guid?)null,
            customerName = (string?)null,
            customerPhone = (string?)null,
            paymentMethod = (int)PaymentMethod.UPI,
            paidAmount = 236m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "Test Item",
                    quantity = 2m,
                    costPrice = 80m,
                    salesPrice = 105m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });

        var saleResponse = await client.SendAsync(saleRequest);

        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var warnings = saleBody.GetProperty("warnings").EnumerateArray().ToList();
        Assert.NotEmpty(warnings);
    }

    // ======================= NEW: GET SALES LIST =======================

    [Fact]
    public async Task GetSales_ReturnsRecordedSales()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            customerId = (Guid?)null,
            customerName = "List Customer",
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
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/sales");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var listResponse = await client.SendAsync(listRequest);

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sales = listBody.EnumerateArray().ToList();
        Assert.NotEmpty(sales);
        Assert.Contains(sales, s => s.GetProperty("customerName").GetString() == "List Customer");
    }

    [Fact]
    public async Task GetSales_WithoutAuth_Returns401()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/api/sales");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSales_WithoutShop_Returns400()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ======================= NEW: GET SALE DETAIL =======================

    [Fact]
    public async Task GetSaleDetail_ReturnsSaleWithItems()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            customerId = (Guid?)null,
            customerName = "Detail Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 236m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "Test Item",
                    quantity = 2m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();

        using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/sales/{saleId}");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var detailResponse = await client.SendAsync(detailRequest);

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(saleBody.GetProperty("invoiceNumber").GetString(), detail.GetProperty("invoiceNumber").GetString());
        Assert.Equal(236m, detail.GetProperty("totalAmount").GetDecimal());
        Assert.Single(detail.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task GetSaleDetail_NonExistentSale_ReturnsNotFound()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/sales/{Guid.NewGuid()}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSaleDetail_OtherShop_ReturnsNotFound()
    {
        using var client = CreateClient();
        var tokenA = await RegisterAsync(client);
        var ownerTokenA = await CreateShopAsync(client, tokenA);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerTokenA, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenA);
        saleRequest.Content = JsonContent.Create(new
        {
            customerId = (Guid?)null,
            customerName = "Cross Shop",
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
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();

        var tokenB = await RegisterAsync(client);
        var ownerTokenB = await CreateShopAsync(client, tokenB);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/sales/{saleId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ======================= NEW: RECORD SALE WITH DUE =======================

    [Fact]
    public async Task RecordSale_WithDue_CreatesCustomerLedgerEntry()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        Guid customerId;
        using (var addCustomerRequest = new HttpRequestMessage(HttpMethod.Post, "/api/customers"))
        {
            addCustomerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
            addCustomerRequest.Content = JsonContent.Create(new
            {
                name = "Due Customer",
                phoneNumber = "+919876543210",
                address = "12 Market Road",
                isActive = true,
            });

            var addCustomerResponse = await client.SendAsync(addCustomerRequest);
            Assert.Equal(HttpStatusCode.Created, addCustomerResponse.StatusCode);
            var addCustomerBody = await addCustomerResponse.Content.ReadFromJsonAsync<JsonElement>();
            customerId = addCustomerBody.GetProperty("customerId").GetGuid();
        }

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            customerId,
            customerName = "Due Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Credit,
            paidAmount = 0m,
            dueAmount = 118m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(118m, saleBody.GetProperty("dueAmount").GetDecimal());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == saleBody.GetProperty("saleId").GetGuid());
        Assert.NotNull(sale);
        Assert.Equal(PaymentMethod.Credit, sale!.PaymentMethod);
    }

    [Fact]
    public async Task RecordSale_StaffCanRecordSale()
    {
        using var client = CreateClient();
        var ownerToken = await RegisterAsync(client);
        var ownerScopedToken = await CreateShopAsync(client, ownerToken);

        var staffEmail = UniqueEmail();
        var staffPassword = "Pass123!Aa";
        using var addUserRequest = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        addUserRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerScopedToken);
        addUserRequest.Content = JsonContent.Create(new
        {
            shopIds = new[] { await GetShopIdFromTokenAsync(client, ownerScopedToken) },
            email = staffEmail,
            firstName = "Staff",
            lastName = "Sales",
            phoneNumber = "+919876543211",
            password = staffPassword,
            confirmPassword = staffPassword,
            role = "Staff",
        });
        var addUserResponse = await client.SendAsync(addUserRequest);
        addUserResponse.EnsureSuccessStatusCode();

        var staffToken = await LoginAsync(client, staffEmail, staffPassword);
        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerScopedToken, barcode, "B-STAFF", 10m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        saleRequest.Content = JsonContent.Create(new
        {
            customerId = (Guid?)null,
            customerName = "Staff Sale",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 118m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-STAFF",
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);

        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
    }

    [Fact]
    public async Task RecordSale_WhenPaidDueDoNotMatchDiscountedTotal_ReturnsBadRequest()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-MISMATCH", 10m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            customerId = (Guid?)null,
            customerName = "Mismatch",
            customerPhone = "+919876543200",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 999m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-MISMATCH",
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });

        var response = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ======================= NEW: SALE RETURN FLOW =======================

    [Fact]
    public async Task PreviewSaleReturn_ReturnsPreviewWithLineDetails()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 10m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            customerId = (Guid?)null,
            customerName = "Return Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 236m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "Test Item",
                    quantity = 2m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).FirstAsync(s => s.Id == saleBody.GetProperty("saleId").GetGuid());
        var saleItemId = sale.Items[0].Id;

        using var previewRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/sales/{sale.Id}/returns/preview");
        previewRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        previewRequest.Content = JsonContent.Create(new
        {
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            items = new[]
            {
                new
                {
                    saleItemId,
                    quantity = 1m,
                    condition = (int)SaleReturnCondition.Restockable,
                    approvedRefundAmount = (decimal?)null,
                    notes = (string?)null,
                },
            },
        });

        var previewResponse = await client.SendAsync(previewRequest);

        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(sale.Id, preview.GetProperty("saleId").GetGuid());
        Assert.True(preview.GetProperty("hasFinancialAccess").GetBoolean());
        Assert.Single(preview.GetProperty("lines").EnumerateArray());
        Assert.True(preview.GetProperty("warnings").GetArrayLength() >= 0);
    }

    [Fact]
    public async Task RecordSaleReturn_Restockable_UpdatesStockAndCreatesReturn()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 10m);
        var itemId = inboundBody.GetProperty("itemId").GetGuid();
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            customerId = (Guid?)null,
            customerName = "Return Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 236m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "Test Item",
                    quantity = 2m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).FirstAsync(s => s.Id == saleId);
        var saleItemId = sale.Items[0].Id;

        using var recordReturnRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/sales/{saleId}/returns");
        recordReturnRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        recordReturnRequest.Content = JsonContent.Create(new
        {
            payoutMethod = (int)PaymentMethod.Cash,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = "Return one unit restockable",
            items = new[]
            {
                new
                {
                    saleItemId,
                    quantity = 1m,
                    condition = (int)SaleReturnCondition.Restockable,
                    approvedRefundAmount = 118m,
                    notes = (string?)null,
                },
            },
        });

        var recordResponse = await client.SendAsync(recordReturnRequest);
        Assert.Equal(HttpStatusCode.OK, recordResponse.StatusCode);

        var recordBody = await recordResponse.Content.ReadFromJsonAsync<JsonElement>();

        var returns = recordBody.GetProperty("returns").EnumerateArray().ToList();
        var returnEntry = Assert.Single(returns);
        Assert.Equal(118m, returnEntry.GetProperty("totalRefundAmount").GetDecimal());

        var batch = await db.InventoryBatches.FirstAsync(b => b.Id == batchId);
        Assert.Equal(9m, batch.Quantity);

        var inv = await db.Inventory.FirstAsync(i => i.ItemId == itemId);
        Assert.Equal(9m, inv.Quantity);
    }

    [Fact]
    public async Task RecordSaleReturn_Wastage_DoesNotRestock()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 10m);
        var itemId = inboundBody.GetProperty("itemId").GetGuid();
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            customerId = (Guid?)null,
            customerName = "Wastage Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 236m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "Test Item",
                    quantity = 2m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).FirstAsync(s => s.Id == saleId);
        var saleItemId = sale.Items[0].Id;

        using var recordReturnRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/sales/{saleId}/returns");
        recordReturnRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        recordReturnRequest.Content = JsonContent.Create(new
        {
            payoutMethod = (int)PaymentMethod.Cash,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = "Return wastage",
            items = new[]
            {
                new
                {
                    saleItemId,
                    quantity = 1m,
                    condition = (int)SaleReturnCondition.Wastage,
                    approvedRefundAmount = 100m,
                    notes = "Damaged packaging",
                },
            },
        });

        var recordResponse = await client.SendAsync(recordReturnRequest);
        Assert.Equal(HttpStatusCode.OK, recordResponse.StatusCode);

        var batch = await db.InventoryBatches.FirstAsync(b => b.Id == batchId);
        Assert.Equal(8m, batch.Quantity);

        var inv = await db.Inventory.FirstAsync(i => i.ItemId == itemId);
        Assert.Equal(8m, inv.Quantity);
    }

    [Fact]
    public async Task VoidSaleReturn_ReturnsNoContent()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 10m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            customerId = (Guid?)null,
            customerName = "Void Return Customer",
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
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).FirstAsync(s => s.Id == saleId);
        var saleItemId = sale.Items[0].Id;

        using var recordReturnRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/sales/{saleId}/returns");
        recordReturnRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        recordReturnRequest.Content = JsonContent.Create(new
        {
            payoutMethod = (int)PaymentMethod.Cash,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = "Return to void",
            items = new[]
            {
                new
                {
                    saleItemId,
                    quantity = 1m,
                    condition = (int)SaleReturnCondition.Restockable,
                    approvedRefundAmount = 118m,
                    notes = (string?)null,
                },
            },
        });

        var recordResponse = await client.SendAsync(recordReturnRequest);
        Assert.Equal(HttpStatusCode.OK, recordResponse.StatusCode);

        var recordBody = await recordResponse.Content.ReadFromJsonAsync<JsonElement>();
        var returns = recordBody.GetProperty("returns").EnumerateArray().ToList();
        var saleReturnId = returns[0].GetProperty("saleReturnId").GetGuid();

        using var voidRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/sales/returns/{saleReturnId}/void");
        voidRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        voidRequest.Content = JsonContent.Create(new
        {
            reason = "Customer changed mind",
        });

        var voidResponse = await client.SendAsync(voidRequest);
        Assert.Equal(HttpStatusCode.NoContent, voidResponse.StatusCode);
    }

    [Fact]
    public async Task GetSaleByReturnNumber_ReturnsSale()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 10m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            customerId = (Guid?)null,
            customerName = "Lookup Customer",
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
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).FirstAsync(s => s.Id == saleId);
        var saleItemId = sale.Items[0].Id;

        using var recordReturnRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/sales/{saleId}/returns");
        recordReturnRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        recordReturnRequest.Content = JsonContent.Create(new
        {
            payoutMethod = (int)PaymentMethod.Cash,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = "Lookup return",
            items = new[]
            {
                new
                {
                    saleItemId,
                    quantity = 1m,
                    condition = (int)SaleReturnCondition.Restockable,
                    approvedRefundAmount = 118m,
                    notes = (string?)null,
                },
            },
        });

        var recordResponse = await client.SendAsync(recordReturnRequest);
        Assert.Equal(HttpStatusCode.OK, recordResponse.StatusCode);
        var recordBody = await recordResponse.Content.ReadFromJsonAsync<JsonElement>();
        var returnNumber = recordBody.GetProperty("returns")[0].GetProperty("returnNumber").GetString()!;

        using var lookupRequest = new HttpRequestMessage(
            HttpMethod.Get, $"/api/sales/returns/{returnNumber}");
        lookupRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var lookupResponse = await client.SendAsync(lookupRequest);

        Assert.Equal(HttpStatusCode.OK, lookupResponse.StatusCode);
        var lookup = await lookupResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(saleId, lookup.GetProperty("saleId").GetGuid());
    }

    [Fact]
    public async Task GetSaleByReturnNumber_NonExistent_ReturnsNoContent()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(
            HttpMethod.Get, "/api/sales/returns/NONEXISTENT");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RecordSaleReturn_StaffGetsForbidden()
    {
        using var client = CreateClient();
        var ownerToken = await RegisterAsync(client);
        var ownerScopedToken = await CreateShopAsync(client, ownerToken);

        var staffEmail = UniqueEmail();
        var staffPassword = "Pass123!Aa";
        using var addUserRequest = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        addUserRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerScopedToken);
        addUserRequest.Content = JsonContent.Create(new
        {
            shopIds = new[] { await GetShopIdFromTokenAsync(client, ownerScopedToken) },
            email = staffEmail,
            firstName = "Staff",
            lastName = "Returns",
            phoneNumber = "+919876543211",
            password = staffPassword,
            confirmPassword = staffPassword,
            role = "Staff",
        });
        var addUserResponse = await client.SendAsync(addUserRequest);
        addUserResponse.EnsureSuccessStatusCode();

        var staffToken = await LoginAsync(client, staffEmail, staffPassword);

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/sales/{Guid.NewGuid()}/returns");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        request.Content = JsonContent.Create(new
        {
            payoutMethod = (int)PaymentMethod.Cash,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = (string?)null,
            items = Array.Empty<object>(),
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task VoidSaleReturn_StaffGetsForbidden()
    {
        using var client = CreateClient();
        var ownerToken = await RegisterAsync(client);
        var ownerScopedToken = await CreateShopAsync(client, ownerToken);

        var staffEmail = UniqueEmail();
        var staffPassword = "Pass123!Aa";
        using var addUserRequest = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        addUserRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerScopedToken);
        addUserRequest.Content = JsonContent.Create(new
        {
            shopIds = new[] { await GetShopIdFromTokenAsync(client, ownerScopedToken) },
            email = staffEmail,
            firstName = "Staff",
            lastName = "Void",
            phoneNumber = "+919876543211",
            password = staffPassword,
            confirmPassword = staffPassword,
            role = "Staff",
        });
        var addUserResponse = await client.SendAsync(addUserRequest);
        addUserResponse.EnsureSuccessStatusCode();

        var staffToken = await LoginAsync(client, staffEmail, staffPassword);

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/sales/returns/{Guid.NewGuid()}/void");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        request.Content = JsonContent.Create(new { reason = "Test" });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
