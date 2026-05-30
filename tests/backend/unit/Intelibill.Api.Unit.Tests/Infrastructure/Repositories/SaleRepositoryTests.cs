using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Repositories;

public sealed class SaleRepositoryTests
{
    private static async Task<ApplicationDbContext> CreateContextAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    [Fact]
    public async Task GetCustomerSalesMetricsAsync_ProvesNullCustomerIdIgnored()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        // Sale with matching customer
        var s1 = Sale.Create(
            shopId,
            "INV-001",
            customerId,
            "Customer1",
            null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            100m,
            0m,
            100m,
            0m,
            []);

        // Sale with null CustomerId
        var s2 = Sale.Create(
            shopId,
            "INV-002",
            null,
            null,
            null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            50m,
            0m,
            50m,
            0m,
            []);

        await context.Sales.AddRangeAsync(s1, s2);
        await context.SaveChangesAsync();

        var repository = new SaleRepository(context);
        var now = DateTime.UtcNow;
        var startUtc = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextStartUtc = startUtc.AddMonths(1);

        var metrics = await repository.GetCustomerSalesMetricsAsync(shopId, [customerId], startUtc, nextStartUtc);

        Assert.True(metrics.ContainsKey(customerId));
        var m = metrics[customerId];
        Assert.Equal(1, m.PurchaseCount);
        Assert.Equal(100m, m.LifetimeRevenue);
    }

    [Fact]
    public async Task GetCustomerSalesMetricsAsync_LifetimeTotalsIncludeAllMatchingSales()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        // 3 sales across different times
        var s1 = Sale.Create(
            shopId,
            "INV-001",
            customerId,
            "Customer1",
            null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow.AddMonths(-2),
            100m,
            0m,
            100m,
            0m,
            []);

        var s2 = Sale.Create(
            shopId,
            "INV-002",
            customerId,
            "Customer1",
            null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow.AddMonths(-1),
            150m,
            0m,
            150m,
            0m,
            []);

        var s3 = Sale.Create(
            shopId,
            "INV-003",
            customerId,
            "Customer1",
            null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            200m,
            0m,
            200m,
            0m,
            []);

        await context.Sales.AddRangeAsync(s1, s2, s3);
        await context.SaveChangesAsync();

        var repository = new SaleRepository(context);
        var now = DateTime.UtcNow;
        var startUtc = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextStartUtc = startUtc.AddMonths(1);

        var metrics = await repository.GetCustomerSalesMetricsAsync(shopId, [customerId], startUtc, nextStartUtc);

        Assert.True(metrics.ContainsKey(customerId));
        var m = metrics[customerId];
        Assert.Equal(3, m.PurchaseCount);
        Assert.Equal(450m, m.LifetimeRevenue);
        Assert.Equal(200m, m.CurrentMonthRevenue); // Only s3 in this month
    }

    [Fact]
    public async Task GetCustomerSalesMetricsAsync_CurrentMonthTotalsOnlyInsideUtcBoundary()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var monthStart = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonthStart = monthStart.AddMonths(1);

        // Sale just before the month start (April 30, 23:59:59 UTC)
        var s1 = Sale.Create(
            shopId,
            "INV-001",
            customerId,
            "Customer1",
            null,
            PaymentMethod.Cash,
            new DateTimeOffset(2026, 4, 30, 23, 59, 59, TimeSpan.Zero),
            100m,
            0m,
            100m,
            0m,
            []);

        // Sale exactly at month start (May 1, 00:00:00 UTC)
        var s2 = Sale.Create(
            shopId,
            "INV-002",
            customerId,
            "Customer1",
            null,
            PaymentMethod.Cash,
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            150m,
            0m,
            150m,
            0m,
            []);

        // Sale exactly just before next month start (May 31, 23:59:59 UTC)
        var s3 = Sale.Create(
            shopId,
            "INV-003",
            customerId,
            "Customer1",
            null,
            PaymentMethod.Cash,
            new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero),
            200m,
            0m,
            200m,
            0m,
            []);

        // Sale exactly at next month start (June 1, 00:00:00 UTC)
        var s4 = Sale.Create(
            shopId,
            "INV-004",
            customerId,
            "Customer1",
            null,
            PaymentMethod.Cash,
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            250m,
            0m,
            250m,
            0m,
            []);

        await context.Sales.AddRangeAsync(s1, s2, s3, s4);
        await context.SaveChangesAsync();

        var repository = new SaleRepository(context);

        var metrics = await repository.GetCustomerSalesMetricsAsync(shopId, [customerId], monthStart, nextMonthStart);

        Assert.True(metrics.ContainsKey(customerId));
        var m = metrics[customerId];
        Assert.Equal(4, m.PurchaseCount); // All 4 sales belong to the customer
        Assert.Equal(700m, m.LifetimeRevenue); // Sum of all 4: 100 + 150 + 200 + 250 = 700
        Assert.Equal(350m, m.CurrentMonthRevenue); // Only s2 and s3 are inside the boundary: 150 + 200 = 350
    }
}
