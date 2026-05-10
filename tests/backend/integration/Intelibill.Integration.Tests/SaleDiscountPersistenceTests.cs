using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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

    [Fact]
    public async Task Migration_BackfillsHistoricalSalesAsZeroDiscountSnapshots()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.DbContainer.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureDeletedAsync();

        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260510103805_AddDiscountRules");

        var shopId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var saleItemId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 5, 10, 8, 30, 0, TimeSpan.Zero);
        var barcode = $"MIG-{Guid.NewGuid():N}";

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO shops (id, name, address, city, state, pincode, created_at)
            VALUES ({shopId}, {"Migration Shop"}, {"42 MG Road"}, {"Bengaluru"}, {"Karnataka"}, {"560001"}, {timestamp});
            """);

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO items (id, shop_id, name, uom, barcode, is_active, created_by, created_at)
            VALUES ({itemId}, {shopId}, {"Migration Item"}, {"pcs"}, {barcode}, {true}, {actorId}, {timestamp});
            """);

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO inventory_batches
                (id, shop_id, item_id, batch_number, quantity, original_quantity, cost_price, mrp, sales_price, tax_rate_percent, tax_included, is_voided, created_by, created_at)
            VALUES
                ({batchId}, {shopId}, {itemId}, {"B-MIG-001"}, {10m}, {10m}, {80m}, {120m}, {100m}, {18m}, {false}, {false}, {actorId}, {timestamp});
            """);

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO sales
                (id, shop_id, invoice_number, payment_method, sold_at, paid_amount, due_amount, total_amount, total_tax_amount, created_at)
            VALUES
                ({saleId}, {shopId}, {"INV-MIG-001"}, {(int)PaymentMethod.Cash}, {timestamp}, {236m}, {0m}, {236m}, {36m}, {timestamp});
            """);

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO sale_items
                (id, sale_id, shop_id, item_id, inventory_batch_id, quantity, cost_price, sales_price, mrp, tax_rate_percent, is_price_including_tax, has_price_mismatch, created_at)
            VALUES
                ({saleItemId}, {saleId}, {shopId}, {itemId}, {batchId}, {2m}, {80m}, {100m}, {120m}, {18m}, {false}, {false}, {timestamp});
            """);

        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();

        var migratedSale = await context.Sales.Include(s => s.Items).SingleAsync(s => s.Id == saleId);
        var migratedItem = Assert.Single(migratedSale.Items);

        Assert.Equal(200m, migratedSale.SubtotalBeforeDiscount);
        Assert.Equal(236m, migratedSale.TotalBeforeDiscount);
        Assert.Equal(0m, migratedSale.TotalDiscountAmount);
        Assert.Equal(236m, migratedSale.TotalAmount);
        Assert.Equal(36m, migratedSale.TotalTaxAmount);
        Assert.Equal(InstantDiscountType.None, migratedSale.SaleDiscountOverrideType);
        Assert.Equal(0m, migratedSale.SaleDiscountOverrideValue);

        Assert.Equal(100m, migratedItem.OriginalSalesPrice);
        Assert.Equal(100m, migratedItem.FinalSalesPrice);
        Assert.Equal(200m, migratedItem.PreTaxAmountBeforeDiscount);
        Assert.Equal(0m, migratedItem.ItemDiscountAmount);
        Assert.Equal(0m, migratedItem.SaleDiscountAmount);
        Assert.Equal(200m, migratedItem.TaxableAmount);
        Assert.Equal(36m, migratedItem.TaxAmount);
        Assert.Equal(236m, migratedItem.TotalAmount);
        Assert.Equal(InstantDiscountType.None, migratedItem.ItemDiscountOverrideType);
        Assert.Equal(0m, migratedItem.ItemDiscountOverrideValue);
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
}
