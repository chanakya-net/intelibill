using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class SaleReturnPersistenceTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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
    public async Task RepositoryFetchesReturnsBySaleAndLinesBySaleItem()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<ISaleReturnRepository>();

        var seeded = await SeedReturnAsync(db);

        var returns = await repository.GetBySaleAsync(seeded.ShopId, seeded.SaleId);
        var lines = await repository.GetLinesBySaleItemAsync(seeded.ShopId, seeded.SaleItemId);

        var saleReturn = Assert.Single(returns);
        Assert.Equal("RET-20260504-ABC123EF", saleReturn.ReturnNumber);
        Assert.Single(saleReturn.Items);

        var line = Assert.Single(lines);
        Assert.Equal(seeded.SaleItemId, line.SaleItemId);
        Assert.Equal(SaleReturnCondition.Restockable, line.Condition);
    }

    [Fact]
    public async Task RepositoryKeepsReturnNumberLookupScopedToShop()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<ISaleReturnRepository>();

        var seeded = await SeedReturnAsync(db);

        var found = await repository.GetByReturnNumberAsync(seeded.ShopId, "RET-20260504-ABC123EF");
        var notFound = await repository.GetByReturnNumberAsync(Guid.NewGuid(), "RET-20260504-ABC123EF");

        Assert.NotNull(found);
        Assert.Null(notFound);
    }

    [Fact]
    public async Task RepositoryPersistsServiceRefundWithoutCondition()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<ISaleReturnRepository>();

        var seeded = await SeedServiceReturnAsync(db);

        var returns = await repository.GetBySaleAsync(seeded.ShopId, seeded.SaleId);
        var line = Assert.Single(Assert.Single(returns).Items);

        Assert.Equal(seeded.SaleItemId, line.SaleItemId);
        Assert.Null(line.Condition);
    }

    private static async Task<(Guid ShopId, Guid SaleId, Guid SaleItemId)> SeedReturnAsync(ApplicationDbContext db)
    {
        var actorId = Guid.NewGuid();
        var shop = Shop.Create(
            $"Return Shop {Guid.NewGuid():N}",
            "42 MG Road",
            "Bengaluru",
            "Karnataka",
            "560001",
            null,
            null,
            null);

        var item = Item.Create(shop.Id, $"Rice {Guid.NewGuid():N}", null, "kg", $"RET-{Guid.NewGuid():N}", true, actorId);
        var batch = InventoryBatch.Create(
            shop.Id,
            item.Id,
            $"B-{Guid.NewGuid():N}",
            10m,
            80m,
            120m,
            100m,
            18m,
            taxIncluded: false,
            expiryDate: null,
            manufacturingDate: null,
            supplierId: null,
            createdBy: actorId).Value;

        var saleItem = SaleItem.CreateGoods(
            shop.Id,
            item.Id,
            batch.Id,
            lineName: item.Name,
            lineCode: item.Barcode,
            2m,
            80m,
            100m,
            120m,
            18m,
            isPriceIncludingTax: false,
            hasPriceMismatch: false);

        var sale = Sale.Create(
            shop.Id,
            $"INV-{Guid.NewGuid():N}",
            customerId: null,
            customerName: null,
            customerPhone: null,
            PaymentMethod.Cash,
            new DateTimeOffset(2026, 5, 4, 9, 0, 0, TimeSpan.Zero),
            paidAmount: 236m,
            dueAmount: 0m,
            totalAmount: 236m,
            totalTaxAmount: 36m,
            [saleItem]);

        var returnLine = SaleReturnItem.Create(
            shop.Id,
            sale.Id,
            saleItem.Id,
            quantity: 1m,
            SaleReturnCondition.Restockable,
            originalCostPrice: 80m,
            originalSalesPrice: 100m,
            originalTaxRatePercent: 18m,
            originalIsPriceIncludingTax: false,
            maxRefundAmount: 118m,
            approvedRefundAmount: 118m,
            taxableAmount: 100m,
            taxAmount: 18m,
            notes: null).Value;

        var line = new SaleReturnLineInput(
            returnLine.ShopId,
            returnLine.SaleItemId,
            returnLine.Quantity,
            returnLine.Condition,
            returnLine.OriginalCostPrice,
            returnLine.OriginalSalesPrice,
            returnLine.OriginalTaxRatePercent,
            returnLine.OriginalIsPriceIncludingTax,
            returnLine.MaxRefundAmount,
            returnLine.ApprovedRefundAmount,
            returnLine.TaxableAmount,
            returnLine.TaxAmount,
            returnLine.Notes);

        var saleReturn = SaleReturn.Record(
            shop.Id,
            sale.Id,
            "RET-20260504-ABC123EF",
            new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero),
            actorId,
            notes: null,
            totalRefundAmount: 118m,
            dueReductionAmount: 0m,
            payoutAmount: 118m,
            payoutDestination: ReturnPayoutDestination.Refund,
            totalTaxableAmount: 100m,
            totalTaxAmount: 18m,
            customerBalanceBefore: null,
            customerBalanceAfter: null,
            [line]).Value;

        db.Shops.Add(shop);
        db.Items.Add(item);
        db.InventoryBatches.Add(batch);
        db.Sales.Add(sale);
        db.SaleReturns.Add(saleReturn);
        await db.SaveChangesAsync();

        return (shop.Id, sale.Id, saleItem.Id);
    }

    private static async Task<(Guid ShopId, Guid SaleId, Guid SaleItemId)> SeedServiceReturnAsync(ApplicationDbContext db)
    {
        var actorId = Guid.NewGuid();
        var shop = Shop.Create(
            $"Service Return Shop {Guid.NewGuid():N}",
            "42 MG Road",
            "Bengaluru",
            "Karnataka",
            "560001",
            null,
            null,
            null);
        var service = Service.Create(
            shop.Id,
            code: $"SVC-{Guid.NewGuid():N}"[..12],
            name: "Consultation",
            description: null,
            price: 150m,
            hsnCode: null,
            taxRatePercent: 0m,
            taxIncluded: true,
            isActive: true,
            createdBy: actorId);

        var serviceItem = SaleItem.CreateService(
            shop.Id,
            service.Id,
            lineName: "Consultation",
            lineCode: $"SRV-{Guid.NewGuid():N}"[..12],
            quantity: 1m,
            costPrice: 0m,
            salesPrice: 150m,
            mrp: 0m,
            taxRatePercent: 0m,
            isPriceIncludingTax: true,
            hasPriceMismatch: false);

        var sale = Sale.Create(
            shop.Id,
            $"INV-SVC-{Guid.NewGuid():N}",
            customerId: null,
            customerName: null,
            customerPhone: null,
            PaymentMethod.Cash,
            new DateTimeOffset(2026, 5, 4, 9, 0, 0, TimeSpan.Zero),
            paidAmount: 150m,
            dueAmount: 0m,
            totalAmount: 150m,
            totalTaxAmount: 0m,
            [serviceItem]);

        var line = new SaleReturnLineInput(
            shop.Id,
            serviceItem.Id,
            1m,
            Condition: null,
            OriginalCostPrice: 0m,
            OriginalSalesPrice: 150m,
            OriginalTaxRatePercent: 0m,
            OriginalIsPriceIncludingTax: true,
            MaxRefundAmount: 150m,
            ApprovedRefundAmount: 150m,
            TaxableAmount: 150m,
            TaxAmount: 0m,
            Notes: null);

        var saleReturn = SaleReturn.Record(
            shop.Id,
            sale.Id,
            "RET-20260504-SERVICE",
            new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero),
            actorId,
            notes: null,
            totalRefundAmount: 150m,
            dueReductionAmount: 0m,
            payoutAmount: 150m,
            payoutDestination: ReturnPayoutDestination.Refund,
            totalTaxableAmount: 150m,
            totalTaxAmount: 0m,
            customerBalanceBefore: null,
            customerBalanceAfter: null,
            [line]).Value;

        db.Shops.Add(shop);
        db.Services.Add(service);
        db.Sales.Add(sale);
        db.SaleReturns.Add(saleReturn);
        await db.SaveChangesAsync();

        return (shop.Id, sale.Id, serviceItem.Id);
    }
}
