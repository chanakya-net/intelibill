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
        Assert.Equal(455m, catalogItem.CurrentStockValue);
        Assert.Equal(455m, result.Summary.TotalStockValue);
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
        Assert.Equal(0, result.Summary.TotalItems);
        Assert.Equal(0m, result.Summary.TotalStockValue);
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
