using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class SaleDiscountPersistenceTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    [Fact]
    public async Task RecordSale_PersistsZeroDiscountSnapshots()
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
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Snapshot Customer",
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

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).SingleAsync(s => s.Id == saleId);
        var saleItem = Assert.Single(sale.Items);

        Assert.Equal(500m, sale.SubtotalBeforeDiscount);
        Assert.Equal(590m, sale.TotalBeforeDiscount);
        Assert.Equal(0m, sale.TotalDiscountAmount);
        Assert.Equal(590m, sale.TotalAmount);
        Assert.Equal(90m, sale.TotalTaxAmount);
        Assert.Equal(InstantDiscountType.None, sale.SaleDiscountOverrideType);
        Assert.Equal(0m, sale.SaleDiscountOverrideValue);
        Assert.Null(sale.ConfiguredSaleRuleId);
        Assert.Null(sale.ConfiguredSaleRuleType);

        Assert.Equal(100m, saleItem.OriginalSalesPrice);
        Assert.Equal(100m, saleItem.FinalSalesPrice);
        Assert.Equal(500m, saleItem.PreTaxAmountBeforeDiscount);
        Assert.Equal(0m, saleItem.ItemDiscountAmount);
        Assert.Equal(0m, saleItem.SaleDiscountAmount);
        Assert.Equal(500m, saleItem.TaxableAmount);
        Assert.Equal(90m, saleItem.TaxAmount);
        Assert.Equal(590m, saleItem.TotalAmount);
        Assert.Equal(InstantDiscountType.None, saleItem.ItemDiscountOverrideType);
        Assert.Equal(0m, saleItem.ItemDiscountOverrideValue);
        Assert.Null(saleItem.ConfiguredBatchRuleId);
        Assert.Null(saleItem.ConfiguredBatchRulePercentage);
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private static string UniqueEmail() => $"sale-discount-{Guid.NewGuid():N}@test.com";
    private static string UniqueBarcode() => $"SALE-DISC-{Guid.NewGuid():N}";

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.EnrollAsync(new
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
            gstNumber = TestTaxProfile.NewGstin(),
            taxProfile = TestTaxProfile.Regular,
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private static async Task<JsonElement> AddInventoryAsync(
        HttpClient client,
        string token,
        string barcode,
        string batchNumber,
        decimal quantity)
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
            totalPurchaseCost = 80m * quantity,
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
}
