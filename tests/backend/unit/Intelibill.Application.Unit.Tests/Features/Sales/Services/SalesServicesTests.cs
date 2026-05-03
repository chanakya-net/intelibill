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

    [Fact]
    public async Task SaleInventoryMutator_WhenValidLine_DeductsStockAndCalculatesIncludedTax()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001", "Rice");
        var batch = MakeBatch(shopId, item.Id, "B-01", quantity: 10m, salesPrice: 110m, taxRatePercent: 10m, taxIncluded: true);
        var inventory = MakeInventory(shopId, item.Id, quantity: 10m);
        var commandLine = new RecordSaleItemCommand("BC-001", "B-01", "Rice", 2m, 80m, 110m, 120m, 10m, true);
        var validatedLine = new ValidatedSaleLine(commandLine, item, batch, inventory, false);
        var txRepository = Substitute.For<IStockTransactionRepository>();

        var mutator = new SaleInventoryMutator(txRepository);
        var result = await mutator.MutateAsync(shopId, "INV-TEST", validatedLine, actorId, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(8m, batch.Quantity);
        Assert.Equal(8m, inventory.Quantity);
        Assert.Equal(20m, result.Value.CalculatedTax);
        Assert.Equal(-2m, result.Value.StockTransaction.Quantity);
        await txRepository.Received(1).AddAsync(result.Value.StockTransaction, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaleAggregator_WhenDueExistsForResolvedCustomer_CreatesSaleAndLedgerEntry()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var customer = Customer.Create(shopId, "Reg User", "+911234567890", null, true);
        var item = MakeItem(shopId, "BC-001", "Rice");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var commandLine = new RecordSaleItemCommand("BC-001", "B-01", "Rice", 1m, 80m, 100m, 120m, 18m, false);
        var saleItem = SaleItem.Create(shopId, item.Id, batch.Id, 1m, 80m, 100m, 120m, 18m, false, false);
        var stockTx = StockTransaction.Create(shopId, item.Id, batch.Id, StockTransactionType.Out, -1m, "INV-TEST", null, DateTimeOffset.UtcNow, actorId, actorId).Value;
        var mutatedLine = new MutatedSaleLine(saleItem, stockTx, 18m);

        var saleRepository = Substitute.For<ISaleRepository>();
        var ledgerRepository = Substitute.For<ICustomerLedgerEntryRepository>();
        var aggregator = new SaleAggregator(saleRepository, ledgerRepository);

        var result = await aggregator.AggregateAsync(
            "INV-TEST",
            shopId,
            paidAmount: 78m,
            dueAmount: 40m,
            actorId,
            customer,
            customerName: null,
            customerPhone: null,
            PaymentMethod.Credit,
            [mutatedLine],
            [],
            new Dictionary<Guid, string> { { item.Id, item.Name } },
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(customer.Id, result.Value.Sale.CustomerId);
        Assert.Equal(118m, result.Value.Sale.TotalAmount);
        Assert.Equal(18m, result.Value.Sale.TotalTaxAmount);
        Assert.NotNull(result.Value.LedgerEntry);
        Assert.Equal(CustomerLedgerEntryType.SaleDue, result.Value.LedgerEntry!.EntryType);
        Assert.Equal(40m, result.Value.LedgerEntry.Amount);
        await saleRepository.Received(1).AddAsync(result.Value.Sale, Arg.Any<CancellationToken>());
        await ledgerRepository.Received(1).AddAsync(result.Value.LedgerEntry, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaleAggregator_WhenPaidAndDueDoNotMatchTotal_ReturnsMismatchError()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001", "Rice");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var saleItem = SaleItem.Create(shopId, item.Id, batch.Id, 1m, 80m, 100m, 120m, 18m, false, false);
        var stockTx = StockTransaction.Create(shopId, item.Id, batch.Id, StockTransactionType.Out, -1m, "INV-TEST", null, DateTimeOffset.UtcNow, actorId, actorId).Value;
        var mutatedLine = new MutatedSaleLine(saleItem, stockTx, 18m);

        var saleRepository = Substitute.For<ISaleRepository>();
        var ledgerRepository = Substitute.For<ICustomerLedgerEntryRepository>();
        var aggregator = new SaleAggregator(saleRepository, ledgerRepository);

        var result = await aggregator.AggregateAsync(
            "INV-TEST",
            shopId,
            paidAmount: 90m,
            dueAmount: 5m,
            actorId,
            resolvedCustomer: null,
            customerName: null,
            customerPhone: null,
            PaymentMethod.Cash,
            [mutatedLine],
            [],
            new Dictionary<Guid, string> { { item.Id, item.Name } },
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.PaidAndDueAmountMismatch.Code, result.FirstError.Code);
        await saleRepository.DidNotReceive().AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
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
