using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class SaleLinePersistenceTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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
    public async Task Db_CanPersistAndLoad_ServiceSaleLine()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var actorId = Guid.NewGuid();
        var shop = Shop.Create("Test Shop", "123 Main St", "City", "State", "560001", null, null, null);
        var shopId = shop.Id;
        var service = Service.Create(
            shopId,
            code: "SRV-001",
            name: "Consultation",
            description: null,
            price: 100m,
            hsnCode: null,
            taxRatePercent: 0m,
            taxIncluded: false,
            isActive: true,
            createdBy: actorId);

        db.Shops.Add(shop);
        db.Services.Add(service);
        await db.SaveChangesAsync();

        var soldAt = DateTimeOffset.UtcNow;
        var line = new SaleLineInput(
            shopId,
            SaleLineType.Service,
            ItemId: null,
            InventoryBatchId: null,
            ServiceId: service.Id,
            LineName: service.Name,
            LineCode: service.Code,
            Quantity: 1m,
            CostPrice: 0m,
            SalesPrice: 100m,
            Mrp: 0m,
            TaxRatePercent: 0m,
            IsPriceIncludingTax: false,
            HasPriceMismatch: false,
            TaxableAmount: 100m,
            TaxAmount: 0m,
            TotalAmount: 100m);

        var sale = Sale.Record(
            shopId,
            actorId,
            $"sale-{Guid.NewGuid():N}",
            "HASH",
            "INV-SRV-001",
            [line],
            customerId: null,
            customerName: null,
            customerPhone: null,
            PaymentMethod.Cash,
            paidAmount: 100m,
            dueAmount: 0m,
            soldAt).Value;

        db.Sales.Add(sale);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var loaded = await db.Sales.Include(s => s.Items).SingleAsync(s => s.Id == sale.Id);
        var loadedLine = Assert.Single(loaded.Items);

        Assert.Equal(SaleLineType.Service, loadedLine.LineType);
        Assert.Equal(service.Id, loadedLine.ServiceId);
        Assert.Null(loadedLine.ItemId);
        Assert.Null(loadedLine.InventoryBatchId);
        Assert.Equal("Consultation", loadedLine.LineName);
        Assert.Equal("SRV-001", loadedLine.LineCode);
    }

    [Fact]
    public async Task Db_WhenInvalidMixedServiceRefs_RejectsRow()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var actorId = Guid.NewGuid();
        var shop = Shop.Create("Test Shop", "123 Main St", "City", "State", "560001", null, null, null);
        var shopId = shop.Id;
        var service = Service.Create(
            shopId,
            code: "SRV-002",
            name: "Repair",
            description: null,
            price: 100m,
            hsnCode: null,
            taxRatePercent: 0m,
            taxIncluded: false,
            isActive: true,
            createdBy: actorId);
        var item = Item.Create(shopId, "Widget", null, "pcs", "BC-001", true, actorId);
        var batch = InventoryBatch.Create(shopId, item.Id, "B-001", quantity: 10m, costPrice: 80m, mrp: 120m, salesPrice: 100m, taxRatePercent: 0m, taxIncluded: false, expiryDate: null, manufacturingDate: null, supplierId: null, createdBy: actorId).Value;
        var sale = Sale.Record(
            shopId,
            actorId,
            $"sale-{Guid.NewGuid():N}",
            "HASH",
            "INV-MIX-001",
            [
                new SaleLineInput(
                    shopId,
                    SaleLineType.Goods,
                    ItemId: item.Id,
                    InventoryBatchId: batch.Id,
                    ServiceId: null,
                    LineName: item.Name,
                    LineCode: item.Barcode,
                    Quantity: 1m,
                    CostPrice: 80m,
                    SalesPrice: 100m,
                    Mrp: 120m,
                    TaxRatePercent: 0m,
                    IsPriceIncludingTax: false,
                    HasPriceMismatch: false,
                    TaxableAmount: 100m,
                    TaxAmount: 0m,
                    TotalAmount: 100m)
            ],
            null,
            null,
            null,
            PaymentMethod.Cash,
            paidAmount: 100m,
            dueAmount: 0m,
            DateTimeOffset.UtcNow).Value;

        db.Shops.Add(shop);
        db.Services.Add(service);
        db.Items.Add(item);
        db.InventoryBatches.Add(batch);
        db.Sales.Add(sale);
        await db.SaveChangesAsync();

        var invalidSaleItemId = Guid.NewGuid();
        var invalidSaleId = sale.Id;

        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO sale_items
                    (id, sale_id, shop_id, line_type, item_id, inventory_batch_id, service_id, line_name, line_code, quantity, cost_price, sales_price, mrp, tax_rate_percent, is_price_including_tax, has_price_mismatch, original_sales_price, final_sales_price, pre_tax_amount_before_discount, item_discount_amount, sale_discount_amount, taxable_amount, tax_amount, total_amount, item_discount_override_type, item_discount_override_value, created_at)
                VALUES
                    ({invalidSaleItemId}, {invalidSaleId}, {shopId}, {"SERVICE"}, {item.Id}, {batch.Id}, {service.Id}, {"Bad"}, {"BAD"}, {1m}, {0m}, {0m}, {0m}, {0m}, {false}, {false}, {0m}, {0m}, {0m}, {0m}, {0m}, {0m}, {0m}, {0m}, {(int)InstantDiscountType.None}, {0m}, {DateTimeOffset.UtcNow});
                """);
        });

        Assert.Contains("ck_sale_items_line_type_refs", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
