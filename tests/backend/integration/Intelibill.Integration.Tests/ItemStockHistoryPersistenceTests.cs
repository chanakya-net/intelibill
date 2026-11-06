using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class ItemStockHistoryPersistenceTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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
    public async Task StockHistory_ReportsMovementsAndTheFinestOnHandScale()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IItemRepository>();

        var actor = Guid.NewGuid();
        var shop = Shop.Create($"History Shop {Guid.NewGuid():N}", "42 MG Road", "Bengaluru", "Karnataka", "560001", null, null, null);
        var untouched = Item.Create(shop.Id, $"Untouched {Guid.NewGuid():N}", null, "kg", $"H-{Guid.NewGuid():N}", true, actor);
        var fractional = Item.Create(shop.Id, $"Fractional {Guid.NewGuid():N}", null, "kg", $"H-{Guid.NewGuid():N}", true, actor);
        var voidedOnly = Item.Create(shop.Id, $"Voided {Guid.NewGuid():N}", null, "kg", $"H-{Guid.NewGuid():N}", true, actor);
        db.Shops.Add(shop);
        db.Items.AddRange(untouched, fractional, voidedOnly);
        db.InventoryBatches.Add(NewBatch(shop.Id, fractional.Id, 2.5m, actor));
        db.InventoryBatches.Add(NewBatch(shop.Id, fractional.Id, 4m, actor));
        db.InventoryBatches.Add(NewBatch(shop.Id, fractional.Id, 0m, actor));
        var voided = NewBatch(shop.Id, voidedOnly.Id, 1.125m, actor);
        voided.Void(actor);
        db.InventoryBatches.Add(voided);
        await db.SaveChangesAsync();

        Assert.Equal(Intelibill.Domain.ValueObjects.ItemStockHistory.None, await repository.GetStockHistoryAsync(shop.Id, untouched.Id));
        Assert.Equal(new Intelibill.Domain.ValueObjects.ItemStockHistory(true, 1), await repository.GetStockHistoryAsync(shop.Id, fractional.Id));
        Assert.Equal(new Intelibill.Domain.ValueObjects.ItemStockHistory(true, 0), await repository.GetStockHistoryAsync(shop.Id, voidedOnly.Id));
    }

    private static InventoryBatch NewBatch(Guid shopId, Guid itemId, decimal quantity, Guid actor) =>
        InventoryBatch.Create(shopId, itemId, $"B-{Guid.NewGuid():N}", quantity, 80m, 120m, 100m, 18m, false, null, null, null, actor).Value;
}
