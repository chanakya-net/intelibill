using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Application.Unit.Tests.Infrastructure.Repositories;

public sealed class InventoryBatchRepositoryTests
{
    [Fact]
    public async Task GetExpiringBatchAlertsAsync_ReturnsPositiveNonVoidedBatchesWithinSevenDays()
    {
        await using var context = await CreateContextAsync();

        var actorId = Guid.NewGuid();
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var banana = Item.Create(shop.Id, "Banana", null, "kg", "B001", true, actorId);
        var apple = Item.Create(shop.Id, "Apple", null, "kg", "B002", true, actorId);
        var milk = Item.Create(shop.Id, "Milk", null, "ltr", "B003", true, actorId);

        var today = new DateOnly(2026, 6, 10);
        var eligibleBatches = new[]
        {
            CreateBatch(shop.Id, banana.Id, "B-002", 1m, today, actorId),
            CreateBatch(shop.Id, apple.Id, "B-001", 2m, today.AddDays(2), actorId),
            CreateBatch(shop.Id, milk.Id, "B-010", 3m, today.AddDays(3), actorId),
            CreateBatch(shop.Id, milk.Id, "B-005", 4m, today.AddDays(3), actorId),
            CreateBatch(shop.Id, banana.Id, "B-900", 5m, today.AddDays(7), actorId),
            CreateBatch(shop.Id, apple.Id, "B-777", 6m, today.AddDays(1), actorId),
        };

        var excludedZeroQuantity = CreateBatch(shop.Id, banana.Id, "B-000", 0m, today.AddDays(1), actorId);
        var excludedVoided = CreateBatch(shop.Id, apple.Id, "B-VOID", 2m, today.AddDays(4), actorId);
        excludedVoided.Void(actorId);
        var excludedLateExpiry = CreateBatch(shop.Id, milk.Id, "B-LATE", 2m, today.AddDays(8), actorId);
        var excludedNoExpiry = CreateBatch(shop.Id, milk.Id, "B-NOEXP", 2m, null, actorId);

        await context.AddRangeAsync(shop, banana, apple, milk);
        await context.AddRangeAsync(eligibleBatches);
        await context.AddRangeAsync(excludedZeroQuantity, excludedVoided, excludedLateExpiry, excludedNoExpiry);
        await context.SaveChangesAsync();

        var repository = new InventoryBatchRepository(context);

        var result = await repository.GetExpiringBatchAlertsAsync(shop.Id, today, CancellationToken.None);

        Assert.Equal(5, result.Count);
        Assert.Equal("B-002", result[0].BatchNumber);
        Assert.Equal("B-777", result[1].BatchNumber);
        Assert.Equal("B-001", result[2].BatchNumber);
        Assert.Equal("B-005", result[3].BatchNumber);
        Assert.Equal("B-010", result[4].BatchNumber);
        Assert.DoesNotContain(result, alert => alert.BatchNumber is "B-000" or "B-VOID" or "B-LATE" or "B-NOEXP");
        Assert.All(result, alert =>
            Assert.True(alert.Quantity > 0m && alert.ExpiryDate >= today && alert.ExpiryDate <= today.AddDays(7)));
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

    private static InventoryBatch CreateBatch(
        Guid shopId,
        Guid itemId,
        string batchNumber,
        decimal quantity,
        DateOnly? expiryDate,
        Guid createdBy)
    {
        return InventoryBatch.Create(
            shopId,
            itemId,
            batchNumber,
            quantity,
            costPrice: 10m,
            mrp: 15m,
            salesPrice: 12m,
            taxRatePercent: 0m,
            taxIncluded: false,
            expiryDate: expiryDate,
            manufacturingDate: null,
            supplierId: null,
            createdBy: createdBy).Value;
    }
}
