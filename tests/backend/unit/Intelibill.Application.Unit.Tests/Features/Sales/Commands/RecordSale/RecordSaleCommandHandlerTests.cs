using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;
using DomainInventory = Intelibill.Domain.Entities.Inventory;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Commands.RecordSale;

public class RecordSaleCommandHandlerTests
{
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();
    private readonly IInventoryBatchRepository _batchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly IInventoryRepository _inventoryRepository = Substitute.For<IInventoryRepository>();
    private readonly IStockTransactionRepository _txRepository = Substitute.For<IStockTransactionRepository>();
    private readonly ICustomerRepository _customerRepository = Substitute.For<ICustomerRepository>();
    private readonly ICustomerLedgerEntryRepository _customerLedgerEntryRepository = Substitute.For<ICustomerLedgerEntryRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private RecordSaleCommandHandler CreateHandler() =>
        new(
            _shopRepository,
            _itemRepository,
            _batchRepository,
            _inventoryRepository,
            _txRepository,
            _customerRepository,
            _customerLedgerEntryRepository,
            _saleRepository,
            _unitOfWork);

    private static Item MakeItem(Guid shopId, string barcode, string name = "Rice")
    {
        return Item.Create(shopId, name, "desc", "kg", barcode, true, Guid.NewGuid());
    }

    private static InventoryBatch MakeBatch(Guid shopId, Guid itemId, string batchNumber, decimal quantity = 100m)
    {
        var result = InventoryBatch.Create(shopId, itemId, batchNumber,
            quantity, costPrice: 80m, mrp: 120m, salesPrice: 100m,
            taxRatePercent: 18m, taxIncluded: false, expiryDate: null,
            manufacturingDate: null, supplierId: null, createdBy: Guid.NewGuid());
        return result.Value;
    }

    private static DomainInventory MakeInventory(Guid shopId, Guid itemId, decimal quantity = 100m)
    {
        var result = DomainInventory.Create(shopId, itemId, quantity, 10m, 500m, Guid.NewGuid());
        return result.Value;
    }

    private static RecordSaleCommand MakeCommand(
        Guid shopId, Guid actorId,
        string barcode = "BC-001", string batchNumber = "B-01",
        decimal quantity = 5m)
    {
        return new RecordSaleCommand(
            actorId, shopId,
            null, "Ravi Kumar", "+919876543210",
            PaymentMethod.Cash,
            quantity * 100m,
            0m,
            [new RecordSaleItemCommand(barcode, batchNumber, "Rice", quantity, 80m, 100m, 120m, 18m, false)]);
    }

    [Fact]
    public async Task HandleAsync_WhenValid_CreatesSaleAndDeductsStock()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01", 100m);
        var inventory = MakeInventory(shopId, item.Id, 100m);

        _itemRepository.GetByBarcodesAsync(shopId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([item]);
        _batchRepository.GetByItemIdsAndBatchNumbersAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([batch]);
        _inventoryRepository.GetByItemIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([inventory]);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(MakeCommand(shopId, actorId), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotEmpty(result.Value.InvoiceNumber);
        Assert.StartsWith("INV-", result.Value.InvoiceNumber);
        Assert.Single(result.Value.Items);
        Assert.Equal("Rice", result.Value.Items[0].ItemName);
        Assert.Equal(95m, batch.Quantity);
        Assert.Equal(95m, inventory.Quantity);
        await _saleRepository.Received(1).AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenBarcodeNotFound_ReturnsError()
    {
        var shopId = Guid.NewGuid();
        _itemRepository.GetByBarcodesAsync(shopId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Item>());

        var handler = CreateHandler();
        var result = await handler.HandleAsync(MakeCommand(shopId, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Sale.ItemNotFound", result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenBatchNotFound_ReturnsError()
    {
        var shopId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");

        _itemRepository.GetByBarcodesAsync(shopId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([item]);
        _batchRepository.GetByItemIdsAndBatchNumbersAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<InventoryBatch>());

        var handler = CreateHandler();
        var result = await handler.HandleAsync(MakeCommand(shopId, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Sale.BatchNotFound", result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenBatchIsVoided_ReturnsError()
    {
        var shopId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        batch.Void(Guid.NewGuid());

        _itemRepository.GetByBarcodesAsync(shopId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([item]);
        _batchRepository.GetByItemIdsAndBatchNumbersAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([batch]);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(MakeCommand(shopId, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Sale.BatchVoided", result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenInsufficientStock_ReturnsError()
    {
        var shopId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01", quantity: 3m);

        _itemRepository.GetByBarcodesAsync(shopId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([item]);
        _batchRepository.GetByItemIdsAndBatchNumbersAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([batch]);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(
            MakeCommand(shopId, Guid.NewGuid(), quantity: 5m),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Sale.InsufficientStock", result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenPriceMismatch_SaleSucceedsWithWarning()
    {
        var shopId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01", 100m); // batch has SalesPrice=100m
        var inventory = MakeInventory(shopId, item.Id, 100m);

        _itemRepository.GetByBarcodesAsync(shopId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([item]);
        _batchRepository.GetByItemIdsAndBatchNumbersAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([batch]);
        _inventoryRepository.GetByItemIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([inventory]);

        // Request SalesPrice=105m, batch has 100m → mismatch
        var command = new RecordSaleCommand(
            Guid.NewGuid(), shopId, null, null, null, PaymentMethod.Cash,
            525m,
            0m,
            [new RecordSaleItemCommand("BC-001", "B-01", "Rice", 5m, 80m, 105m, 120m, 18m, false)]);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotEmpty(result.Value.Warnings);
        Assert.True(result.Value.Items[0].HasPriceMismatch);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenItemNameMismatch_SaleSucceedsWithWarning()
    {
        var shopId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001", "Rice"); // actual item name = "Rice"
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);

        _itemRepository.GetByBarcodesAsync(shopId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([item]);
        _batchRepository.GetByItemIdsAndBatchNumbersAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([batch]);
        _inventoryRepository.GetByItemIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([inventory]);

        // Command sends "Wheat" but the item is "Rice"
        var command = new RecordSaleCommand(
            Guid.NewGuid(), shopId, null, null, null, PaymentMethod.Cash,
            100m,
            0m,
            [new RecordSaleItemCommand("BC-001", "B-01", "Wheat", 1m, 80m, 100m, 120m, 18m, false)]);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotEmpty(result.Value.Warnings);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithMultipleItems_CallsSaveChangesOnce()
    {
        var shopId = Guid.NewGuid();
        var item1 = MakeItem(shopId, "BC-001", "Rice");
        var item2 = MakeItem(shopId, "BC-002", "Dal");
        var batch1 = MakeBatch(shopId, item1.Id, "B-01");
        var batch2 = MakeBatch(shopId, item2.Id, "B-02");
        var inv1 = MakeInventory(shopId, item1.Id);
        var inv2 = MakeInventory(shopId, item2.Id);

        _itemRepository.GetByBarcodesAsync(shopId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([item1, item2]);
        _batchRepository.GetByItemIdsAndBatchNumbersAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([batch1, batch2]);
        _inventoryRepository.GetByItemIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([inv1, inv2]);

        var command = new RecordSaleCommand(
            Guid.NewGuid(), shopId, null, null, null, PaymentMethod.UPI,
            740m,
            0m,
            [
                new RecordSaleItemCommand("BC-001", "B-01", "Rice", 5m, 80m, 100m, 120m, 18m, false),
                new RecordSaleItemCommand("BC-002", "B-02", "Dal", 3m, 60m, 80m, 100m, 5m, false),
            ]);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Items.Count);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_TaxExcluded_ComputesTotalTaxAmountCorrectly()
    {
        // qty=2, salesPrice=100, taxRate=10%, excluded → taxAmount = 2 * 100 * 10 / 100 = 20
        var shopId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);

        _itemRepository.GetByBarcodesAsync(shopId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([item]);
        _batchRepository.GetByItemIdsAndBatchNumbersAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([batch]);
        _inventoryRepository.GetByItemIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([inventory]);

        var command = new RecordSaleCommand(
            Guid.NewGuid(), shopId, null, null, null, PaymentMethod.Card,
            200m,
            0m,
            [new RecordSaleItemCommand("BC-001", "B-01", "Rice", 2m, 80m, 100m, 120m, 10m, false)]);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(20m, result.Value.TotalTaxAmount);
    }

    [Fact]
    public async Task HandleAsync_TaxIncluded_ComputesTotalTaxAmountCorrectly()
    {
        // qty=1, salesPrice=110, taxRate=10%, included → taxAmount = 1 * 110 * 10 / (100 + 10) = 10
        var shopId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);

        _itemRepository.GetByBarcodesAsync(shopId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([item]);
        _batchRepository.GetByItemIdsAndBatchNumbersAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([batch]);
        _inventoryRepository.GetByItemIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([inventory]);

        var command = new RecordSaleCommand(
            Guid.NewGuid(), shopId, null, null, null, PaymentMethod.Card,
            110m,
            0m,
            [new RecordSaleItemCommand("BC-001", "B-01", "Rice", 1m, 80m, 110m, 120m, 10m, true)]);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(10m, result.Value.TotalTaxAmount);
    }

    [Fact]
    public async Task HandleAsync_WalkInCustomer_StoresNameAndPhone()
    {
        var shopId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);

        _itemRepository.GetByBarcodesAsync(shopId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([item]);
        _batchRepository.GetByItemIdsAndBatchNumbersAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([batch]);
        _inventoryRepository.GetByItemIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([inventory]);

        var command = new RecordSaleCommand(
            Guid.NewGuid(), shopId,
            null, "Walk-In Customer", "+911234567890",
            PaymentMethod.Cash,
            100m,
            0m,
            [new RecordSaleItemCommand("BC-001", "B-01", "Rice", 1m, 80m, 100m, 120m, 18m, false)]);

        Sale? capturedSale = null;
        _saleRepository.When(r => r.AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>()))
            .Do(ci => capturedSale = ci.Arg<Sale>());

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(capturedSale);
        Assert.Null(capturedSale!.CustomerId);
        Assert.Equal("Walk-In Customer", capturedSale.CustomerName);
        Assert.Equal("+911234567890", capturedSale.CustomerPhone);
    }

    [Fact]
    public async Task HandleAsync_RegularCustomer_StoresCustomerId()
    {
        var shopId = Guid.NewGuid();
        var shop = Shop.Create("My Shop", "Addr", "City", "State", "560001", null, null, null);
        var ownerMembership = ShopMembership.Create(shop.Id, Guid.NewGuid(), ShopRole.Owner, false);
        shop.AddMembership(ownerMembership);
        var customer = Customer.Create(ownerMembership.UserId, "Reg User", "+911234567890", null, true);
        var customerId = customer.Id;
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);

        _itemRepository.GetByBarcodesAsync(shopId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([item]);
        _batchRepository.GetByItemIdsAndBatchNumbersAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([batch]);
        _inventoryRepository.GetByItemIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([inventory]);
        _shopRepository.GetByIdWithMembersAsync(shopId, Arg.Any<CancellationToken>()).Returns(shop);
        _customerRepository.GetByOwnerAndIdAsync(ownerMembership.UserId, customerId, Arg.Any<CancellationToken>()).Returns(customer);

        var command = new RecordSaleCommand(
            Guid.NewGuid(), shopId,
            customerId, null, null,
            PaymentMethod.UPI,
            100m,
            0m,
            [new RecordSaleItemCommand("BC-001", "B-01", "Rice", 1m, 80m, 100m, 120m, 18m, false)]);

        Sale? capturedSale = null;
        _saleRepository.When(r => r.AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>()))
            .Do(ci => capturedSale = ci.Arg<Sale>());

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(capturedSale);
        Assert.Equal(customerId, capturedSale!.CustomerId);
    }

    [Fact]
    public async Task HandleAsync_WhenPaidAndDueDoNotMatchTotal_ReturnsValidationError()
    {
        var shopId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);

        _itemRepository.GetByBarcodesAsync(shopId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([item]);
        _batchRepository.GetByItemIdsAndBatchNumbersAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([batch]);
        _inventoryRepository.GetByItemIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([inventory]);

        var command = new RecordSaleCommand(
            Guid.NewGuid(), shopId,
            null, null, null,
            PaymentMethod.Cash,
            90m,
            5m,
            [new RecordSaleItemCommand("BC-001", "B-01", "Rice", 1m, 80m, 100m, 120m, 18m, false)]);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Sale.PaidAndDueAmountMismatch", result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenDueExistsAndCustomerMissing_ReturnsNotFoundError()
    {
        var shopId = Guid.NewGuid();
        var shop = Shop.Create("My Shop", "Addr", "City", "State", "560001", null, null, null);
        var ownerMembership = ShopMembership.Create(shop.Id, Guid.NewGuid(), ShopRole.Owner, false);
        shop.AddMembership(ownerMembership);
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);

        _itemRepository.GetByBarcodesAsync(shopId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([item]);
        _batchRepository.GetByItemIdsAndBatchNumbersAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([batch]);
        _inventoryRepository.GetByItemIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([inventory]);
        _shopRepository.GetByIdWithMembersAsync(shopId, Arg.Any<CancellationToken>()).Returns(shop);
        _customerRepository.GetByOwnerAndPhoneAsync(ownerMembership.UserId, "+911234567890", Arg.Any<CancellationToken>())
            .Returns((Customer?)null);

        var command = new RecordSaleCommand(
            Guid.NewGuid(), shopId,
            null, "Walk-In", "+911234567890",
            PaymentMethod.Cash,
            80m,
            20m,
            [new RecordSaleItemCommand("BC-001", "B-01", "Rice", 1m, 80m, 100m, 120m, 18m, false)]);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Sale.CreditCustomerNotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenDueExistsAndCustomerResolved_CreatesCustomerLedgerEntry()
    {
        var shopId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var shop = Shop.Create("My Shop", "Addr", "City", "State", "560001", null, null, null);
        var ownerMembership = ShopMembership.Create(shop.Id, Guid.NewGuid(), ShopRole.Owner, false);
        shop.AddMembership(ownerMembership);
        var customer = Customer.Create(ownerMembership.UserId, "Reg User", "+911234567890", null, true);
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);

        _itemRepository.GetByBarcodesAsync(shopId, Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([item]);
        _batchRepository.GetByItemIdsAndBatchNumbersAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([batch]);
        _inventoryRepository.GetByItemIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([inventory]);
        _shopRepository.GetByIdWithMembersAsync(shopId, Arg.Any<CancellationToken>()).Returns(shop);
        _customerRepository.GetByOwnerAndPhoneAsync(ownerMembership.UserId, "+911234567890", Arg.Any<CancellationToken>())
            .Returns(customer);

        var command = new RecordSaleCommand(
            actorUserId, shopId,
            null, "Reg User", "+911234567890",
            PaymentMethod.Credit,
            60m,
            40m,
            [new RecordSaleItemCommand("BC-001", "B-01", "Rice", 1m, 80m, 100m, 120m, 18m, false)]);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        await _customerLedgerEntryRepository.Received(1).AddAsync(
            Arg.Is<CustomerLedgerEntry>(entry =>
                entry.CustomerId == customer.Id
                && entry.EntryType == CustomerLedgerEntryType.SaleDue
                && entry.Amount == 40m),
            Arg.Any<CancellationToken>());
    }
}
