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

public class SalesControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public SalesControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private static string UniqueEmail() => $"sales-{Guid.NewGuid():N}@test.com";
    private static string UniqueBarcode() => $"SALE-{Guid.NewGuid():N}";

    // Helper: register user, get token
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

    // Helper: create shop, return owner-scoped token
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

    // Helper: seed item + batch + inventory via inventory inbound
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

        // Record a sale of 5 units
        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            customerId = (Guid?)null,
            customerName = "Walk-in Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 500m,
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
                },
            },
        });

        var saleResponse = await client.SendAsync(saleRequest);

        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);

        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();
        var invoiceNumber = saleBody.GetProperty("invoiceNumber").GetString()!;

        Assert.StartsWith("INV-", invoiceNumber);
        Assert.Equal(500m, saleBody.GetProperty("totalAmount").GetDecimal());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Verify sale record exists with items
        var sale = await db.Sales.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == saleId);
        Assert.NotNull(sale);
        Assert.Single(sale!.Items);
        Assert.Equal(500m, sale.TotalAmount);

        // Verify stock transaction (Out, negative quantity)
        var tx = await db.StockTransactions
            .FirstOrDefaultAsync(t => t.InventoryBatchId == batchId && t.TransactionType == StockTransactionType.Out);
        Assert.NotNull(tx);
        Assert.Equal(-5m, tx!.Quantity);
        Assert.Equal(invoiceNumber, tx.ReferenceNumber);

        // Verify batch quantity deducted (50 - 5 = 45)
        var batch = await db.InventoryBatches.FirstOrDefaultAsync(b => b.Id == batchId);
        Assert.NotNull(batch);
        Assert.Equal(45m, batch!.Quantity);

        // Verify inventory aggregate deducted (50 - 5 = 45)
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
        await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);

        // Send with different salesPrice than batch (100m → 105m)
        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            customerId = (Guid?)null,
            customerName = (string?)null,
            customerPhone = (string?)null,
            paymentMethod = (int)PaymentMethod.UPI,
            paidAmount = 210m,
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
                    salesPrice = 105m,   // differs from batch's 100m → mismatch
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                },
            },
        });

        var saleResponse = await client.SendAsync(saleRequest);

        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var warnings = saleBody.GetProperty("warnings").EnumerateArray().ToList();
        Assert.NotEmpty(warnings);
    }
}
