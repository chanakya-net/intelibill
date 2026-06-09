using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Application.Unit.Tests.Infrastructure.Repositories;

public sealed class SaleRepositoryTests
{
    [Fact]
    public async Task GetLatestDashboardSalesAsync_ReturnsLatestFiveSalesForActiveShop()
    {
        await using var context = await CreateContextAsync();

        var actorId = Guid.NewGuid();
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var otherShop = Shop.Create("Other", "Address", "City", "State", "560002", null, null, null);

        var sales = new[]
        {
            CreateSale(shop.Id, actorId, "INV-001", null, new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero), 100m),
            CreateSale(shop.Id, actorId, "INV-002", "Asha", new DateTimeOffset(2026, 5, 2, 9, 0, 0, TimeSpan.Zero), 200m),
            CreateSale(shop.Id, actorId, "INV-003", "Ben", new DateTimeOffset(2026, 5, 3, 9, 0, 0, TimeSpan.Zero), 300m),
            CreateSale(shop.Id, actorId, "INV-004", "Cora", new DateTimeOffset(2026, 5, 4, 9, 0, 0, TimeSpan.Zero), 400m),
            CreateSale(shop.Id, actorId, "INV-005", "Dinesh", new DateTimeOffset(2026, 5, 5, 9, 0, 0, TimeSpan.Zero), 500m),
            CreateSale(shop.Id, actorId, "INV-006", null, new DateTimeOffset(2026, 5, 6, 9, 0, 0, TimeSpan.Zero), 600m),
        };
        var otherSale = CreateSale(otherShop.Id, actorId, "INV-OTHER", "Other", new DateTimeOffset(2026, 5, 7, 9, 0, 0, TimeSpan.Zero), 700m);

        await context.AddRangeAsync(shop, otherShop, otherSale, sales[0], sales[1], sales[2], sales[3], sales[4], sales[5]);
        await context.SaveChangesAsync();

        var repository = new SaleRepository(context);

        var result = await repository.GetLatestDashboardSalesAsync(shop.Id, CancellationToken.None);

        Assert.Equal(5, result.Count);
        Assert.Equal("INV-006", result[0].InvoiceNumber);
        Assert.Equal("INV-005", result[1].InvoiceNumber);
        Assert.Equal("INV-004", result[2].InvoiceNumber);
        Assert.Equal("INV-003", result[3].InvoiceNumber);
        Assert.Equal("INV-002", result[4].InvoiceNumber);
        Assert.Equal("Walk-in Customer", result[0].CustomerDisplayName);
        Assert.DoesNotContain(result, sale => sale.InvoiceNumber == "INV-001");
        Assert.DoesNotContain(result, sale => sale.InvoiceNumber == "INV-OTHER");
    }

    [Fact]
    public async Task GetDailySalesTrendAsync_ReturnsGroupedRowsInAscendingOrder()
    {
        await using var context = await CreateContextAsync();

        var actorId = Guid.NewGuid();
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);

        var firstSaleItem = SaleItem.CreateGoods(shop.Id, Guid.NewGuid(), Guid.NewGuid(), "Item A", "BC-A", 1m, 80m, 100m, 100m, 0m, false, false);
        var secondSaleItem = SaleItem.CreateGoods(shop.Id, Guid.NewGuid(), Guid.NewGuid(), "Item B", "BC-B", 1m, 160m, 200m, 200m, 0m, false, false);

        var firstSale = Sale.Create(shop.Id, actorId, "idem-1", "hash-1", "INV-001", null, null, null, PaymentMethod.Cash, new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero), 100m, 0m, 100m, 0m, [firstSaleItem]);
        var secondSale = Sale.Create(shop.Id, actorId, "idem-2", "hash-2", "INV-002", null, null, null, PaymentMethod.Cash, new DateTimeOffset(2026, 5, 3, 9, 0, 0, TimeSpan.Zero), 200m, 0m, 200m, 0m, [secondSaleItem]);

        var activeReturn = SaleReturn.Record(
            shop.Id,
            firstSale.Id,
            "RET-001",
            new DateTimeOffset(2026, 5, 2, 9, 0, 0, TimeSpan.Zero),
            actorId,
            null,
            30m,
            0m,
            30m,
            PaymentMethod.Cash,
            30m,
            0m,
            null,
            null,
            [
                new SaleReturnLineInput(
                    shop.Id,
                    firstSaleItem.Id,
                    1m,
                    SaleReturnCondition.Restockable,
                    80m,
                    100m,
                    0m,
                    false,
                    30m,
                    30m,
                    30m,
                    0m,
                    null),
            ]).Value;

        var voidedReturn = SaleReturn.Record(
            shop.Id,
            secondSale.Id,
            "RET-002",
            new DateTimeOffset(2026, 5, 3, 12, 0, 0, TimeSpan.Zero),
            actorId,
            null,
            999m,
            0m,
            999m,
            PaymentMethod.Cash,
            999m,
            0m,
            null,
            null,
            [
                new SaleReturnLineInput(
                    shop.Id,
                    secondSaleItem.Id,
                    1m,
                    SaleReturnCondition.Restockable,
                    160m,
                    200m,
                    0m,
                    false,
                    999m,
                    999m,
                    999m,
                    0m,
                    null),
            ]).Value;
        _ = voidedReturn.Void(new DateTimeOffset(2026, 5, 3, 13, 0, 0, TimeSpan.Zero), actorId, "Cancelled").Value;

        await context.AddRangeAsync(shop, firstSale, secondSale, activeReturn, voidedReturn);
        await context.SaveChangesAsync();

        var repository = new SaleRepository(context);

        var result = await repository.GetDailySalesTrendAsync(
            shop.Id,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 3),
            CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Equal(new DateOnly(2026, 5, 1), result[0].Date);
        Assert.Equal(100m, result[0].GrossSales);
        Assert.Equal(0m, result[0].ReturnAmount);
        Assert.Equal(new DateOnly(2026, 5, 2), result[1].Date);
        Assert.Equal(0m, result[1].GrossSales);
        Assert.Equal(30m, result[1].ReturnAmount);
        Assert.Equal(new DateOnly(2026, 5, 3), result[2].Date);
        Assert.Equal(200m, result[2].GrossSales);
        Assert.Equal(0m, result[2].ReturnAmount);
    }

    private static async Task<ApplicationDbContext> CreateContextAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private static Sale CreateSale(
        Guid shopId,
        Guid actorId,
        string invoiceNumber,
        string? customerName,
        DateTimeOffset soldAt,
        decimal totalAmount) =>
        Sale.Create(
            shopId,
            actorId,
            $"idem-{invoiceNumber}",
            $"hash-{invoiceNumber}",
            invoiceNumber,
            null,
            customerName,
            null,
            PaymentMethod.Cash,
            soldAt,
            totalAmount,
            0m,
            totalAmount,
            0m,
            Array.Empty<SaleItem>());
}
