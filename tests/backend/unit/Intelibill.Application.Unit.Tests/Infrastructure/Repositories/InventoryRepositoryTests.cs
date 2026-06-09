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
