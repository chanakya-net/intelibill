using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class InventoryAdjustmentPersistenceTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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
    public async Task RepositoryFetchesAdjustmentByNumberAndBatchScopedToShop()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IInventoryAdjustmentRepository>();

        var seeded = await SeedAdjustmentAsync(db);

        var byNumber = await repository.GetByAdjustmentNumberAsync(seeded.ShopId, "ADJ-20260505-0001");
        var wrongShop = await repository.GetByAdjustmentNumberAsync(Guid.NewGuid(), "ADJ-20260505-0001");
        var byBatch = await repository.GetByBatchAsync(seeded.ShopId, seeded.BatchId);

        Assert.NotNull(byNumber);
        Assert.Equal(seeded.AdjustmentId, byNumber.Id);
        Assert.Null(wrongShop);

        var adjustment = Assert.Single(byBatch);
        Assert.Equal(InventoryAdjustmentReason.Damaged, adjustment.Reason);
        Assert.Equal(2m, adjustment.Quantity);
    }

    private static async Task<(Guid ShopId, Guid BatchId, Guid AdjustmentId)> SeedAdjustmentAsync(ApplicationDbContext db)
    {
        var actorId = Guid.NewGuid();
        var shop = Shop.Create(
            $"Adjustment Shop {Guid.NewGuid():N}",
            "42 MG Road",
            "Bengaluru",
            "Karnataka",
            "560001",
            null,
            null,
            null);

        var item = Item.Create(shop.Id, $"Rice {Guid.NewGuid():N}", null, "kg", $"ADJ-{Guid.NewGuid():N}", true, actorId);
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

        var adjustment = InventoryAdjustment.Create(
            shop.Id,
            item.Id,
            batch.Id,
            "ADJ-20260505-0001",
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Damaged,
            quantity: 2m,
            unitCost: 80m,
            costImpact: 160m,
            batchQuantityBefore: 10m,
            batchQuantityAfter: 8m,
            inventoryQuantityBefore: 10m,
            inventoryQuantityAfter: 8m,
            new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero),
            actorId,
            notes: null,
            createdBy: actorId).Value;

        db.Shops.Add(shop);
        db.Items.Add(item);
        db.InventoryBatches.Add(batch);
        db.InventoryAdjustments.Add(adjustment);
        await db.SaveChangesAsync();

        return (shop.Id, batch.Id, adjustment.Id);
    }
}
