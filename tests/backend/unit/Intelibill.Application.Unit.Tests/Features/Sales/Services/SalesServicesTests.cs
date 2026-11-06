using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
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
    public async Task SaleLineValidator_WhenHsnCodeInvalid_ReturnsError()
    {
        var shopId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001", "Rice");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);
        var commandLine = new RecordSaleItemCommand("BC-001", "B-01", "Rice", 1m, 80m, 100m, 120m, 18m, false, batch.Id, HsnCode: "ABC");

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
        var result = await validator.ValidateLinesAsync(shopId, [commandLine], new List<string>(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.InvalidHsnCode.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task SaleLineValidator_WhenTaxRateScaleInvalid_ReturnsError()
    {
        var shopId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001", "Rice");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);
        var commandLine = new RecordSaleItemCommand("BC-001", "B-01", "Rice", 1m, 80m, 100m, 120m, 10.123m, false, batch.Id);

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
        var result = await validator.ValidateLinesAsync(shopId, [commandLine], new List<string>(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.InvalidTaxRatePercent.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task SaleLineValidator_WhenTaxRateOutOfRange_ReturnsError()
    {
        var shopId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001", "Rice");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);
        var commandLine = new RecordSaleItemCommand("BC-001", "B-01", "Rice", 1m, 80m, 100m, 120m, 120m, false, batch.Id);

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
        var result = await validator.ValidateLinesAsync(shopId, [commandLine], new List<string>(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.InvalidTaxRatePercent.Code, result.FirstError.Code);
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

    [Fact]
    public async Task SaleLineValidator_WhenDuplicateBatchLinesExceedCumulativeStock_ReturnsInsufficientStock()
    {
        var shopId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001", "Rice");
        var batch = MakeBatch(shopId, item.Id, "B-01", quantity: 100m);
        var inventory = MakeInventory(shopId, item.Id);
        var firstLine = new RecordSaleItemCommand("BC-001", "B-01", "Rice", 60m, 80m, 100m, 120m, 18m, false, batch.Id);
        var secondLine = new RecordSaleItemCommand("BC-001", "B-01", "Rice", 60m, 80m, 100m, 120m, 18m, false, batch.Id);

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
        var result = await validator.ValidateLinesAsync(shopId, [firstLine, secondLine], new List<string>(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Sale.InsufficientStock", result.FirstError.Code);
    }

    [Fact]
    public async Task SaleLineValidator_WhenItemInactive_ReturnsItemInactive()
    {
        var shopId = Guid.NewGuid();
        var item = Item.Create(shopId, "Rice", "desc", "kg", "BC-001", false, Guid.NewGuid());
        var batch = MakeBatch(shopId, item.Id, "B-01", quantity: 100m);
        var inventory = MakeInventory(shopId, item.Id);
        var commandLine = new RecordSaleItemCommand("BC-001", "B-01", "Rice", 1m, 80m, 100m, 120m, 18m, false, batch.Id);

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
        var result = await validator.ValidateLinesAsync(shopId, [commandLine], new List<string>(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Sale.ItemInactive", result.FirstError.Code);
    }

    [Fact]
    public async Task SaleLineValidator_WhenLineTypeIsUnsupported_ReturnsInvalidLineType()
    {
        var shopId = Guid.NewGuid();
        var itemRepository = Substitute.For<IItemRepository>();
        var batchRepository = Substitute.For<IInventoryBatchRepository>();
        var inventoryRepository = Substitute.For<IInventoryRepository>();
        var commandLine = new RecordSaleItemCommand(
            "BC-001",
            "B-01",
            "Rice",
            1m,
            80m,
            100m,
            120m,
            18m,
            false,
            Guid.NewGuid(),
            LineType: (SaleLineType)999);

        var validator = new SaleLineValidator(itemRepository, batchRepository, inventoryRepository);
        var result = await validator.ValidateLinesAsync(shopId, [commandLine], new List<string>(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.InvalidLineType.Code, result.FirstError.Code);
    }

    [Theory]
    [InlineData("kg", "0.25", null)]
    [InlineData("PCS", "0.5", "Sale.QuantityPrecisionExceeded")]
    [InlineData("kg", "0.0001", "Sale.QuantityPrecisionExceeded")]
    [InlineData("PCS", "2.0", null)]
    public async Task SaleLineValidator_AppliesItemQuantityPolicy(string uom, string quantity, string? expectedError)
    {
        var shopId = Guid.NewGuid();
        var item = Item.Create(shopId, "Rice", "desc", uom, "BC-001", true, Guid.NewGuid());
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);
        var line = new RecordSaleItemCommand("BC-001", "B-01", "Rice", decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), 80m, 100m, 120m, 18m, false, batch.Id);

        var itemRepository = Substitute.For<IItemRepository>();
        var batchRepository = Substitute.For<IInventoryBatchRepository>();
        var inventoryRepository = Substitute.For<IInventoryRepository>();
        batchRepository.GetByIdWithItemAsync(batch.Id, shopId, Arg.Any<CancellationToken>()).Returns(batch);
        itemRepository.GetByIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns([item]);
        inventoryRepository.GetByItemIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns([inventory]);

        var result = await new SaleLineValidator(itemRepository, batchRepository, inventoryRepository)
            .ValidateLinesAsync(shopId, [line], [], CancellationToken.None);

        if (expectedError is null)
            Assert.False(result.IsError);
        else
            Assert.Equal(expectedError, result.FirstError.Code);
    }

    [Fact]
    public async Task SaleLineValidator_WhenServiceQuantityHasFourDecimals_ReturnsPrecisionError()
    {
        var shopId = Guid.NewGuid();
        var service = Service.Create(shopId, "SVC-001", "Repair", null, 500m, null, 18m, false, true, Guid.NewGuid());
        var serviceRepository = Substitute.For<IServiceRepository>();
        serviceRepository.GetByIdAsync(service.Id, Arg.Any<CancellationToken>()).Returns(service);
        var line = new RecordSaleItemCommand("", "", "Repair", 1.0005m, 0m, 500m, 0m, 18m, false, Guid.Empty, LineType: SaleLineType.Service, ServiceId: service.Id);

        var result = await new SaleLineValidator(
                Substitute.For<IItemRepository>(),
                Substitute.For<IInventoryBatchRepository>(),
                Substitute.For<IInventoryRepository>(),
                serviceRepository)
            .ValidateLinesAsync(shopId, [line], [], CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Sale.QuantityPrecisionExceeded", result.FirstError.Code);
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
