using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Services;

public class SalesServicesTests
{
    [Fact]
    public async Task SaleLineValidator_WhenPriceAndNameMismatch_ReturnsPairedLineAndWarnings()
    {
        var shopId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001", "Rice");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);
        var commandLine = new RecordSaleItemCommand("BC-001", "B-01", "Wheat", 2m, 81m, 101m, 121m, 19m, false, batch.Id);
        var warnings = new List<string>();

        var itemRepository = Substitute.For<IItemRepository>();
        var batchRepository = Substitute.For<IInventoryBatchRepository>();
        var inventoryRepository = Substitute.For<IInventoryRepository>();

        batchRepository.GetByIdWithItemAsync(batch.Id, shopId, Arg.Any<CancellationToken>())
            .Returns(batch);
        itemRepository.GetByIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([item]);
        inventoryRepository.GetByItemIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([inventory]);

        var validator = new SaleLineValidator(itemRepository, batchRepository, inventoryRepository);
        var result = await validator.ValidateLinesAsync(shopId, [commandLine], warnings, CancellationToken.None);

        Assert.False(result.IsError);
        var line = Assert.Single(result.Value.Lines);
        Assert.Same(commandLine, line.Command);
        Assert.True(line.HasPriceMismatch);
        Assert.Equal(2, warnings.Count);
        await itemRepository.Received(1).GetByIdsAsync(shopId, Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { item.Id })), Arg.Any<CancellationToken>());
        await batchRepository.Received(1).GetByIdWithItemAsync(batch.Id, shopId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaleLineValidator_WhenBatchIdMissing_ReturnsBatchNotFound()
    {
        var shopId = Guid.NewGuid();
        var itemRepository = Substitute.For<IItemRepository>();
        var batchRepository = Substitute.For<IInventoryBatchRepository>();
        var inventoryRepository = Substitute.For<IInventoryRepository>();
        var commandLine = new RecordSaleItemCommand("BC-001", "B-01", "Rice", 1m, 80m, 100m, 120m, 18m, false, Guid.NewGuid());

        batchRepository.GetByIdWithItemAsync(commandLine.InventoryBatchId, shopId, Arg.Any<CancellationToken>())
            .Returns((InventoryBatch?)null);

        var validator = new SaleLineValidator(itemRepository, batchRepository, inventoryRepository);
        var result = await validator.ValidateLinesAsync(shopId, [commandLine], new List<string>(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Sale.BatchNotFound", result.FirstError.Code);
        await batchRepository.Received(1).GetByIdWithItemAsync(commandLine.InventoryBatchId, shopId, Arg.Any<CancellationToken>());
    }

    private static Item MakeItem(Guid shopId, string barcode, string name) =>
        Item.Create(shopId, name, "desc", "kg", barcode, true, Guid.NewGuid());

    private static InventoryBatch MakeBatch(
        Guid shopId,
        Guid itemId,
        string batchNumber,
        decimal quantity = 100m,
        decimal salesPrice = 100m,
        decimal taxRatePercent = 18m,
        bool taxIncluded = false) =>
        InventoryBatch.Create(
            shopId,
            itemId,
            batchNumber,
            quantity,
            costPrice: 80m,
            mrp: 120m,
            salesPrice,
            taxRatePercent,
            taxIncluded,
            expiryDate: null,
            manufacturingDate: null,
            supplierId: null,
            createdBy: Guid.NewGuid()).Value;

    private static Domain.Entities.Inventory MakeInventory(Guid shopId, Guid itemId, decimal quantity = 100m) =>
        Domain.Entities.Inventory.Create(shopId, itemId, quantity, reorderLevel: 10m, maxLevel: 500m, createdBy: Guid.NewGuid()).Value;
}
