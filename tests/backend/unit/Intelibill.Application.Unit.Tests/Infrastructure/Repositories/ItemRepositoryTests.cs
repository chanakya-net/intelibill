using System.Reflection;
using Intelibill.Application.Features.Items.Queries.GetItems;
using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Application.Unit.Tests.Infrastructure.Repositories;

public sealed class ItemRepositoryTests
{
    [Fact]
    public async Task GetCatalogAsync_WhenMultipleBatchesExist_UsesLatestBatchPriceForStockValue()
    {
        await using var context = await CreateContextAsync();

        var actorId = Guid.NewGuid();
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var item = Item.Create(shop.Id, "Milk", null, "ltr", "B001", true, actorId);
        var inventory = Inventory.Create(shop.Id, item.Id, 7m, 3m, 20m, actorId).Value;

        var olderBatch = InventoryBatch.Create(
            shop.Id,
            item.Id,
            "BATCH-OLD",
            5m,
            40m,
            60m,
            45m,
            0m,
            false,
            null,
            null,
            null,
            actorId).Value;
        var newerBatch = InventoryBatch.Create(
            shop.Id,
            item.Id,
            "BATCH-NEW",
            2m,
            50m,
            70m,
            65m,
            0m,
            false,
            null,
            null,
            null,
            actorId).Value;

        SetCreatedAt(olderBatch, new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
        SetCreatedAt(newerBatch, new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero));

        await context.AddRangeAsync(shop, item, inventory, olderBatch, newerBatch);
        await context.SaveChangesAsync();

        var repository = new ItemRepository(context);

        var result = await repository.GetCatalogAsync(new ItemCatalogFilter(shop.Id, null, null, 1, 20));

        var catalogItem = Assert.Single(result.Items);
        Assert.Equal(65m, catalogItem.UnitPrice);
        Assert.Equal(355m, catalogItem.CurrentStockValue);
        Assert.Equal(355m, result.Summary.TotalStockValue);
    }

    [Fact]
    public async Task GetCatalogAsync_WhenStatusFilterIsUnknown_ReturnsNoItems()
    {
        await using var context = await CreateContextAsync();

        var actorId = Guid.NewGuid();
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var item = Item.Create(shop.Id, "Milk", null, "ltr", "B001", true, actorId);

        await context.AddRangeAsync(shop, item);
        await context.SaveChangesAsync();

        var repository = new ItemRepository(context);
	        var result = await repository.GetCatalogAsync(new ItemCatalogFilter(shop.Id, null, "disabled", 1, 20));

	        Assert.Empty(result.Items);
	        Assert.Equal(0, result.TotalCount);
	        Assert.Equal(1, result.Summary.TotalItems);
	        Assert.Equal(1, result.Summary.ActiveItems);
	        Assert.Equal(0, result.Summary.InactiveItems);
	        Assert.Equal(0, result.Summary.RunningLowStockCount);
	        Assert.Equal(1, result.Summary.CriticalStockCount);
	        Assert.Equal(0m, result.Summary.TotalStockValue);
	    }

    [Fact]
    public async Task GetCatalogAsync_ComputesSummaryOverAllItems_RegardlessOfPagination()
    {
        await using var context = await CreateContextAsync();

        var actorId = Guid.NewGuid();
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);

        var item1 = Item.Create(shop.Id, "Milk 1", null, "ltr", "B001", true, actorId);
        var item2 = Item.Create(shop.Id, "Milk 2", null, "ltr", "B002", true, actorId);
        var item3 = Item.Create(shop.Id, "Milk 3", null, "ltr", "B003", false, actorId); // inactive

        var inventory1 = Inventory.Create(shop.Id, item1.Id, 10m, 5m, 20m, actorId).Value;
        var inventory2 = Inventory.Create(shop.Id, item2.Id, 2m, 5m, 20m, actorId).Value; // running low

        var batch1 = InventoryBatch.Create(shop.Id, item1.Id, "B1", 10m, 10m, 15m, 12m, 0m, false, null, null, null, actorId).Value;
        var batch2 = InventoryBatch.Create(shop.Id, item2.Id, "B2", 2m, 20m, 25m, 22m, 0m, false, null, null, null, actorId).Value;

        await context.AddRangeAsync(shop, item1, item2, item3, inventory1, inventory2, batch1, batch2);
        await context.SaveChangesAsync();

        var repository = new ItemRepository(context);

        // Fetch page 1 with page size 1
        var result = await repository.GetCatalogAsync(new ItemCatalogFilter(shop.Id, null, null, 1, 1));

        // Paged items should only contain 1 item
        Assert.Single(result.Items);
        Assert.Equal(3, result.TotalCount);

        // Summary should reflect ALL matching items (3 items: 2 active, 1 inactive)
        Assert.Equal(3, result.Summary.TotalItems);
        Assert.Equal(2, result.Summary.ActiveItems);
        Assert.Equal(1, result.Summary.InactiveItems);
        Assert.Equal(1, result.Summary.RunningLowStockCount); // Milk 2 is running low (2 <= 5)
        Assert.Equal(0, result.Summary.CriticalStockCount); // inactive items do not count as stock critical
        Assert.Equal(10m * 12m + 2m * 22m, result.Summary.TotalStockValue); // 120 + 44 = 164
    }

    [Fact]
    public async Task GetCatalogAsync_WhenPagingBeyondLastPage_ReturnsCorrectSummary()
    {
        await using var context = await CreateContextAsync();

        var actorId = Guid.NewGuid();
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);

        var item = Item.Create(shop.Id, "Milk", null, "ltr", "B001", true, actorId);
        var inventory = Inventory.Create(shop.Id, item.Id, 5m, 2m, 20m, actorId).Value;
        var batch = InventoryBatch.Create(shop.Id, item.Id, "B1", 5m, 10m, 15m, 12m, 0m, false, null, null, null, actorId).Value;

        await context.AddRangeAsync(shop, item, inventory, batch);
        await context.SaveChangesAsync();

        var repository = new ItemRepository(context);

        // Fetch page 2 with page size 10 (beyond last page)
        var result = await repository.GetCatalogAsync(new ItemCatalogFilter(shop.Id, null, null, 2, 10));

        Assert.Empty(result.Items);
        Assert.Equal(1, result.TotalCount);

        // Summary should be populated correctly even though we are beyond the last page
        Assert.Equal(1, result.Summary.TotalItems);
        Assert.Equal(1, result.Summary.ActiveItems);
        Assert.Equal(0, result.Summary.InactiveItems);
        Assert.Equal(0, result.Summary.RunningLowStockCount);
        Assert.Equal(0, result.Summary.CriticalStockCount);
        Assert.Equal(60m, result.Summary.TotalStockValue); // 5 * 12
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

    private static void SetCreatedAt(BaseEntity entity, DateTimeOffset createdAt)
    {
        typeof(BaseEntity)
            .GetProperty(nameof(BaseEntity.CreatedAt), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(entity, createdAt);
    }
}
