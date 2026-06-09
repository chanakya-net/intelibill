using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Application.Unit.Tests.Infrastructure.Repositories;

public sealed class InventoryRepositoryTests
{
    [Fact]
    public async Task CountLowStockItemsByShopAsync_IgnoresZeroReorderLevels()
    {
        await using var context = await CreateContextAsync();

        var actorId = Guid.NewGuid();
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var item1 = Item.Create(shop.Id, "Milk", null, "ltr", "B001", true, actorId);
        var item2 = Item.Create(shop.Id, "Bread", null, "pcs", "B002", true, actorId);
        var inventory1 = Inventory.Create(shop.Id, item1.Id, 0m, 0m, 20m, actorId).Value;
        var inventory2 = Inventory.Create(shop.Id, item2.Id, 2m, 5m, 20m, actorId).Value;

        await context.AddRangeAsync(shop, item1, item2, inventory1, inventory2);
        await context.SaveChangesAsync();

        var repository = new InventoryRepository(context);

        var count = await repository.CountLowStockItemsByShopAsync(shop.Id);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CountLowStockItemsByShopAsync_CountsItemsAtOrBelowPositiveReorderLevel()
    {
        await using var context = await CreateContextAsync();

        var actorId = Guid.NewGuid();
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var item1 = Item.Create(shop.Id, "Milk", null, "ltr", "B001", true, actorId);
        var item2 = Item.Create(shop.Id, "Bread", null, "pcs", "B002", true, actorId);
        var item3 = Item.Create(shop.Id, "Rice", null, "kg", "B003", true, actorId);
        var inventory1 = Inventory.Create(shop.Id, item1.Id, 5m, 5m, 20m, actorId).Value;
        var inventory2 = Inventory.Create(shop.Id, item2.Id, 4m, 5m, 20m, actorId).Value;
        var inventory3 = Inventory.Create(shop.Id, item3.Id, 6m, 5m, 20m, actorId).Value;

        await context.AddRangeAsync(shop, item1, item2, item3, inventory1, inventory2, inventory3);
        await context.SaveChangesAsync();

        var repository = new InventoryRepository(context);

        var count = await repository.CountLowStockItemsByShopAsync(shop.Id);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetTopLowStockAlertsAsync_IgnoresItemsWithZeroReorderLevel()
    {
        await using var context = await CreateContextAsync();
        var actorId = Guid.NewGuid();
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var item1 = Item.Create(shop.Id, "Milk", null, "ltr", "B001", true, actorId);
        var item2 = Item.Create(shop.Id, "Bread", null, "pcs", "B002", true, actorId);
        var inv1 = Inventory.Create(shop.Id, item1.Id, 0m, 0m, 20m, actorId).Value;
        var inv2 = Inventory.Create(shop.Id, item2.Id, 2m, 5m, 20m, actorId).Value;
        await context.AddRangeAsync(shop, item1, item2, inv1, inv2);
        await context.SaveChangesAsync();

        var repo = new InventoryRepository(context);
        var alerts = await repo.GetTopLowStockAlertsAsync(shop.Id);

        Assert.Single(alerts);
        Assert.Equal(item2.Id, alerts[0].ItemId);
    }

    [Fact]
    public async Task GetTopLowStockAlertsAsync_ExcludesItemsAboveReorderLevel()
    {
        await using var context = await CreateContextAsync();
        var actorId = Guid.NewGuid();
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var item1 = Item.Create(shop.Id, "Milk", null, "ltr", "B001", true, actorId);
        var item2 = Item.Create(shop.Id, "Bread", null, "pcs", "B002", true, actorId);
        var inv1 = Inventory.Create(shop.Id, item1.Id, 10m, 5m, 20m, actorId).Value;
        var inv2 = Inventory.Create(shop.Id, item2.Id, 3m, 5m, 20m, actorId).Value;
        await context.AddRangeAsync(shop, item1, item2, inv1, inv2);
        await context.SaveChangesAsync();

        var repo = new InventoryRepository(context);
        var alerts = await repo.GetTopLowStockAlertsAsync(shop.Id);

        Assert.Single(alerts);
        Assert.Equal(item2.Id, alerts[0].ItemId);
    }

    [Fact]
    public async Task GetTopLowStockAlertsAsync_ReturnsAtMostFiveItems()
    {
        await using var context = await CreateContextAsync();
        var actorId = Guid.NewGuid();
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var items = Enumerable.Range(1, 7).Select(i =>
            Item.Create(shop.Id, $"Item{i:D2}", null, "pcs", $"B{i:D3}", true, actorId)).ToList();
        var inventories = items.Select(item =>
            Inventory.Create(shop.Id, item.Id, 1m, 5m, 20m, actorId).Value).ToList();
        await context.AddAsync(shop);
        await context.AddRangeAsync(items);
        await context.AddRangeAsync(inventories);
        await context.SaveChangesAsync();

        var repo = new InventoryRepository(context);
        var alerts = await repo.GetTopLowStockAlertsAsync(shop.Id);

        Assert.Equal(5, alerts.Count);
    }

    [Fact]
    public async Task GetTopLowStockAlertsAsync_OrderedByQuantityRatioThenItemName()
    {
        await using var context = await CreateContextAsync();
        var actorId = Guid.NewGuid();
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var itemA = Item.Create(shop.Id, "Almonds", null, "kg", "B001", true, actorId);
        var itemB = Item.Create(shop.Id, "Biscuits", null, "pcs", "B002", true, actorId);
        var itemC = Item.Create(shop.Id, "Cashews", null, "kg", "B003", true, actorId);
        // Ratio: A=0.1, B=0.1, C=0.5 → A and B first (tied on ratio, alphabetical order), then C
        var invA = Inventory.Create(shop.Id, itemA.Id, 1m, 10m, 20m, actorId).Value;
        var invB = Inventory.Create(shop.Id, itemB.Id, 1m, 10m, 20m, actorId).Value;
        var invC = Inventory.Create(shop.Id, itemC.Id, 5m, 10m, 20m, actorId).Value;
        await context.AddRangeAsync(shop, itemA, itemB, itemC, invA, invB, invC);
        await context.SaveChangesAsync();

        var repo = new InventoryRepository(context);
        var alerts = await repo.GetTopLowStockAlertsAsync(shop.Id);

        Assert.Equal(3, alerts.Count);
        Assert.Equal("Almonds", alerts[0].ItemName);
        Assert.Equal("Biscuits", alerts[1].ItemName);
        Assert.Equal("Cashews", alerts[2].ItemName);
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
}
