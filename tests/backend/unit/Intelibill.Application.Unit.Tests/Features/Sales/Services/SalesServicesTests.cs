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
        var commandLine = new RecordSaleItemCommand("BC-001", "B-01", "Wheat", 2m, 81m, 101m, 121m, 19m, false);
        var warnings = new List<string>();

        var itemRepository = Substitute.For<IItemRepository>();
        var batchRepository = Substitute.For<IInventoryBatchRepository>();
        var inventoryRepository = Substitute.For<IInventoryRepository>();

        itemRepository.GetByBarcodesAsync(shopId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([item]);
        batchRepository.GetByItemIdsAndBatchNumbersAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([batch]);
        inventoryRepository.GetByItemIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([inventory]);

        var validator = new SaleLineValidator(itemRepository, batchRepository, inventoryRepository);
        var result = await validator.ValidateLinesAsync(shopId, [commandLine], warnings, CancellationToken.None);

        Assert.False(result.IsError);
        var line = Assert.Single(result.Value.Lines);
        Assert.Same(commandLine, line.Command);
        Assert.True(line.HasPriceMismatch);
        Assert.Equal(2, warnings.Count);
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
