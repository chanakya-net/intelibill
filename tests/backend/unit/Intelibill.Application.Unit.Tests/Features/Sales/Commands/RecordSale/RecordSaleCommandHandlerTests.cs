using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Commands.RecordSale;

public class RecordSaleCommandHandlerTests
{
    private readonly ISaleLineValidator _saleLineValidator = Substitute.For<ISaleLineValidator>();
    private readonly ISaleInventoryMutator _saleInventoryMutator = Substitute.For<ISaleInventoryMutator>();
    private readonly ICustomerResolver _customerResolver = Substitute.For<ICustomerResolver>();
    private readonly ISaleAggregator _saleAggregator = Substitute.For<ISaleAggregator>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public RecordSaleCommandHandlerTests()
    {
        _customerResolver.ResolveAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Customer?>>((Customer?)null));
    }

    private RecordSaleCommandHandler CreateHandler() =>
        new(_saleLineValidator, _saleInventoryMutator, _customerResolver, _saleAggregator, _unitOfWork);

    private static Item MakeItem(Guid shopId, string barcode, string name = "Rice") =>
        Item.Create(shopId, name, "desc", "kg", barcode, true, Guid.NewGuid());

    private static InventoryBatch MakeBatch(Guid shopId, Guid itemId, string batchNumber, decimal quantity = 100m)
    {
        var result = InventoryBatch.Create(shopId, itemId, batchNumber,
            quantity, costPrice: 80m, mrp: 120m, salesPrice: 100m,
            taxRatePercent: 18m, taxIncluded: false, expiryDate: null,
            manufacturingDate: null, supplierId: null, createdBy: Guid.NewGuid());
        return result.Value;
    }

    private static Domain.Entities.Inventory MakeInventory(Guid shopId, Guid itemId, decimal quantity = 100m)
    {
        var result = Domain.Entities.Inventory.Create(shopId, itemId, quantity, 10m, 500m, Guid.NewGuid());
        return result.Value;
    }

    private static RecordSaleCommand MakeCommand(
        Guid shopId, Guid actorId,
        string barcode = "BC-001", string batchNumber = "B-01",
        decimal quantity = 5m) =>
        new(actorId, shopId, null, "Ravi Kumar", "+919876543210",
            PaymentMethod.Cash, quantity * 118m, 0m,
            [new RecordSaleItemCommand(barcode, batchNumber, "Rice", quantity, 80m, 100m, 120m, 18m, false)]);

    [Fact]
    public async Task HandleAsync_WhenValid_CreatesSaleAndDeductsStock()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01", 100m);
        var inventory = MakeInventory(shopId, item.Id, 100m);

        var command = MakeCommand(shopId, actorId);
        var line = new ValidatedSaleLine(command.Items[0], item, batch, inventory, false);
        var itemNameById = new Dictionary<Guid, string> { { item.Id, item.Name } };
        var validResult1 = new SaleLineValidationResult(new List<ValidatedSaleLine> { line }, itemNameById);
        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(validResult1);

        var si = SaleItem.Create(shopId, item.Id, batch.Id, 5m, 80m, 100m, 120m, 18m, false, false);
        var tx = StockTransaction.Create(shopId, item.Id, batch.Id, StockTransactionType.Out, -5m, "INV-TEST", null, DateTimeOffset.UtcNow, actorId, actorId).Value;
        _saleInventoryMutator.MutateAsync(shopId, Arg.Any<string>(), line, actorId, Arg.Any<CancellationToken>())
            .Returns(new MutatedSaleLine(si, tx, 90m));

        var dto = new SaleDto(Guid.NewGuid(), "INV-TEST-00000001", null, PaymentMethod.Cash, DateTimeOffset.UtcNow, 590m, 0m, 590m, 90m,
            [new SaleItemDto(si.Id, si.ItemId, "Rice", si.InventoryBatchId, 5m, 100m, 18m, false, false)], []);
        _saleAggregator.AggregateAsync(Arg.Any<string>(), shopId, Arg.Any<decimal>(), Arg.Any<decimal>(),
                actorId, Arg.Any<Customer?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<PaymentMethod>(), Arg.Any<IReadOnlyList<MutatedSaleLine>>(),
                Arg.Any<List<string>>(), Arg.Any<IReadOnlyDictionary<Guid, string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleAggregation(null!, null, dto));

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotEmpty(result.Value.InvoiceNumber);
        Assert.StartsWith("INV-", result.Value.InvoiceNumber);
        Assert.Single(result.Value.Items);
        Assert.Equal("Rice", result.Value.Items[0].ItemName);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenBarcodeNotFound_ReturnsError()
    {
        ErrorOr<SaleLineValidationResult> itemNotFound = Errors.Sale.ItemNotFound("BC-001");
        _saleLineValidator.ValidateLinesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(),
                Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(itemNotFound);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(MakeCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Sale.ItemNotFound", result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenBatchNotFound_ReturnsError()
    {
        ErrorOr<SaleLineValidationResult> batchNotFound = Errors.Sale.BatchNotFound("BC-001", "B-01");
        _saleLineValidator.ValidateLinesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(),
                Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(batchNotFound);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(MakeCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Sale.BatchNotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenInsufficientStock_ReturnsError()
    {
        ErrorOr<SaleLineValidationResult> insufficientStock = Errors.Sale.InsufficientStock("BC-001", "B-01");
        _saleLineValidator.ValidateLinesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(),
                Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(insufficientStock);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(MakeCommand(Guid.NewGuid(), Guid.NewGuid(), quantity: 5m), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Sale.InsufficientStock", result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WithMultipleItems_CallsSaveChangesOnce()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item1 = MakeItem(shopId, "BC-001", "Rice");
        var item2 = MakeItem(shopId, "BC-002", "Dal");
        var batch1 = MakeBatch(shopId, item1.Id, "B-01");
        var batch2 = MakeBatch(shopId, item2.Id, "B-02");
        var inv1 = MakeInventory(shopId, item1.Id);
        var inv2 = MakeInventory(shopId, item2.Id);

        var command = new RecordSaleCommand(actorId, shopId, null, null, null, PaymentMethod.UPI, 842m, 0m,
            [new RecordSaleItemCommand("BC-001", "B-01", "Rice", 5m, 80m, 100m, 120m, 18m, false),
             new RecordSaleItemCommand("BC-002", "B-02", "Dal", 3m, 60m, 80m, 100m, 5m, false)]);

        var line1 = new ValidatedSaleLine(command.Items[0], item1, batch1, inv1, false);
        var line2 = new ValidatedSaleLine(command.Items[1], item2, batch2, inv2, false);

        var itemNameById = new Dictionary<Guid, string> { { item1.Id, "Rice" }, { item2.Id, "Dal" } };
        var validResult2 = new SaleLineValidationResult(new List<ValidatedSaleLine> { line1, line2 }, itemNameById);
        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(validResult2);

        var si1 = SaleItem.Create(shopId, item1.Id, batch1.Id, 5m, 80m, 100m, 120m, 18m, false, false);
        var si2 = SaleItem.Create(shopId, item2.Id, batch2.Id, 3m, 60m, 80m, 100m, 5m, false, false);
        var tx1 = StockTransaction.Create(shopId, item1.Id, batch1.Id, StockTransactionType.Out, -5m, "INV-TEST", null, DateTimeOffset.UtcNow, actorId, actorId).Value;
        var tx2 = StockTransaction.Create(shopId, item2.Id, batch2.Id, StockTransactionType.Out, -3m, "INV-TEST", null, DateTimeOffset.UtcNow, actorId, actorId).Value;

        _saleInventoryMutator.MutateAsync(shopId, Arg.Any<string>(), line1, actorId, Arg.Any<CancellationToken>())
            .Returns(new MutatedSaleLine(si1, tx1, 90m));
        _saleInventoryMutator.MutateAsync(shopId, Arg.Any<string>(), line2, actorId, Arg.Any<CancellationToken>())
            .Returns(new MutatedSaleLine(si2, tx2, 12m));

        var dto = new SaleDto(Guid.NewGuid(), "INV-TEST", null, PaymentMethod.UPI, DateTimeOffset.UtcNow, 842m, 0m, 842m, 102m,
            [new SaleItemDto(si1.Id, si1.ItemId, "Rice", si1.InventoryBatchId, 5m, 100m, 18m, false, false),
             new SaleItemDto(si2.Id, si2.ItemId, "Dal", si2.InventoryBatchId, 3m, 80m, 5m, false, false)], []);
        _saleAggregator.AggregateAsync(Arg.Any<string>(), shopId, 842m, 0m, actorId, Arg.Any<Customer?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                PaymentMethod.UPI, Arg.Any<IReadOnlyList<MutatedSaleLine>>(), Arg.Any<List<string>>(),
                Arg.Any<IReadOnlyDictionary<Guid, string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleAggregation(null!, null, dto));

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Items.Count);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenNoRegisteredCustomer_SavesGuestNameAndPhone()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);
        var command = new RecordSaleCommand(actorId, shopId, null, "Guest Raj", "+919999999999", PaymentMethod.Cash, 590m, 0m,
            [new RecordSaleItemCommand("BC-001", "B-01", "Rice", 5m, 80m, 100m, 120m, 18m, false)]);
        var line = new ValidatedSaleLine(command.Items[0], item, batch, inventory, false);

        var itemNameById = new Dictionary<Guid, string> { { item.Id, item.Name } };
        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(),
                Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult(new List<ValidatedSaleLine> { line }, itemNameById));

        var si = SaleItem.Create(shopId, item.Id, batch.Id, 5m, 80m, 100m, 120m, 18m, false, false);
        var tx = StockTransaction.Create(shopId, item.Id, batch.Id, StockTransactionType.Out, -5m, "INV-TEST", null, DateTimeOffset.UtcNow, actorId, actorId).Value;
        _saleInventoryMutator.MutateAsync(shopId, Arg.Any<string>(), line, actorId, Arg.Any<CancellationToken>())
            .Returns(new MutatedSaleLine(si, tx, 90m));

        Customer? capturedCustomer = null;
        string? capturedName = null;
        string? capturedPhone = null;
        _saleAggregator.AggregateAsync(
                Arg.Any<string>(), shopId, 590m, 0m, actorId,
                Arg.Do<Customer?>(c => capturedCustomer = c),
                Arg.Do<string?>(n => capturedName = n),
                Arg.Do<string?>(p => capturedPhone = p),
                PaymentMethod.Cash,
                Arg.Any<IReadOnlyList<MutatedSaleLine>>(),
                Arg.Any<List<string>>(),
                Arg.Any<IReadOnlyDictionary<Guid, string>>(),
                Arg.Any<CancellationToken>())
            .Returns(new SaleAggregation(null!, null, new SaleDto(
                Guid.NewGuid(), "INV-TEST-00000001", null, PaymentMethod.Cash, DateTimeOffset.UtcNow,
                590m, 0m, 590m, 90m,
                [new SaleItemDto(si.Id, si.ItemId, "Rice", si.InventoryBatchId, 5m, 100m, 18m, false, false)], [])));

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Null(capturedCustomer);
        Assert.Equal("Guest Raj", capturedName);
        Assert.Equal("+919999999999", capturedPhone);
    }

    [Fact]
    public async Task HandleAsync_WhenSecondLineMutationFails_ReturnsErrorWithoutSaving()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var item1 = MakeItem(shopId, "BC-001", "Rice");
        var item2 = MakeItem(shopId, "BC-002", "Dal");
        var batch1 = MakeBatch(shopId, item1.Id, "B-01");
        var batch2 = MakeBatch(shopId, item2.Id, "B-02");
        var inv1 = MakeInventory(shopId, item1.Id);
        var inv2 = MakeInventory(shopId, item2.Id);

        var command = new RecordSaleCommand(actorId, shopId, null, "Guest", "+911111111111", PaymentMethod.Cash, 842m, 0m,
            [
                new RecordSaleItemCommand("BC-001", "B-01", "Rice", 5m, 80m, 100m, 120m, 18m, false),
                new RecordSaleItemCommand("BC-002", "B-02", "Dal", 3m, 60m, 80m, 100m, 5m, false)
            ]);

        var line1 = new ValidatedSaleLine(command.Items[0], item1, batch1, inv1, false);
        var line2 = new ValidatedSaleLine(command.Items[1], item2, batch2, inv2, false);
        var itemNameById = new Dictionary<Guid, string> { { item1.Id, "Rice" }, { item2.Id, "Dal" } };

        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult(new List<ValidatedSaleLine> { line1, line2 }, itemNameById));

        var si1 = SaleItem.Create(shopId, item1.Id, batch1.Id, 5m, 80m, 100m, 120m, 18m, false, false);
        var tx1 = StockTransaction.Create(shopId, item1.Id, batch1.Id, StockTransactionType.Out, -5m, "INV-TEST", null, DateTimeOffset.UtcNow, actorId, actorId).Value;
        _saleInventoryMutator.MutateAsync(shopId, Arg.Any<string>(), line1, actorId, Arg.Any<CancellationToken>())
            .Returns(new MutatedSaleLine(si1, tx1, 90m));
        _saleInventoryMutator.MutateAsync(shopId, Arg.Any<string>(), line2, actorId, Arg.Any<CancellationToken>())
            .Returns(Errors.Sale.InsufficientStock("BC-002", "B-02"));

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.InsufficientStock("BC-002", "B-02").Code, result.FirstError.Code);
        await _saleAggregator.DidNotReceive().AggregateAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<decimal>(),
            Arg.Any<Guid>(), Arg.Any<Customer?>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<PaymentMethod>(), Arg.Any<IReadOnlyList<MutatedSaleLine>>(), Arg.Any<List<string>>(),
            Arg.Any<IReadOnlyDictionary<Guid, string>>(), Arg.Any<CancellationToken>());
        await _customerResolver.DidNotReceive().ResolveAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenCustomerResolutionFails_ReturnsErrorWithoutSaving()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);

        var command = new RecordSaleCommand(actorId, shopId, null, "Walk In", "+919999999999", PaymentMethod.Credit, 78m, 40m,
            [new RecordSaleItemCommand("BC-001", "B-01", "Rice", 1m, 80m, 100m, 120m, 18m, false)]);
        var line = new ValidatedSaleLine(command.Items[0], item, batch, inventory, false);
        var itemNameById = new Dictionary<Guid, string> { { item.Id, item.Name } };

        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult(new List<ValidatedSaleLine> { line }, itemNameById));

        var saleItem = SaleItem.Create(shopId, item.Id, batch.Id, 1m, 80m, 100m, 120m, 18m, false, false);
        var tx = StockTransaction.Create(shopId, item.Id, batch.Id, StockTransactionType.Out, -1m, "INV-TEST", null, DateTimeOffset.UtcNow, actorId, actorId).Value;
        _saleInventoryMutator.MutateAsync(shopId, Arg.Any<string>(), line, actorId, Arg.Any<CancellationToken>())
            .Returns(new MutatedSaleLine(saleItem, tx, 18m));

        _customerResolver.ResolveAsync(shopId, command.CustomerId, command.CustomerPhone, true, PaymentMethod.Credit, Arg.Any<CancellationToken>())
            .Returns(Errors.Sale.CreditCustomerNotFound);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.CreditCustomerNotFound.Code, result.FirstError.Code);
        await _saleAggregator.DidNotReceive().AggregateAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<decimal>(),
            Arg.Any<Guid>(), Arg.Any<Customer?>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<PaymentMethod>(), Arg.Any<IReadOnlyList<MutatedSaleLine>>(), Arg.Any<List<string>>(),
            Arg.Any<IReadOnlyDictionary<Guid, string>>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenAggregatorReturnsValidationError_DoesNotSaveChanges()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);

        var command = MakeCommand(shopId, actorId, quantity: 1m);
        var line = new ValidatedSaleLine(command.Items[0], item, batch, inventory, false);
        var itemNameById = new Dictionary<Guid, string> { { item.Id, item.Name } };

        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult(new List<ValidatedSaleLine> { line }, itemNameById));

        var saleItem = SaleItem.Create(shopId, item.Id, batch.Id, 1m, 80m, 100m, 120m, 18m, false, false);
        var tx = StockTransaction.Create(shopId, item.Id, batch.Id, StockTransactionType.Out, -1m, "INV-TEST", null, DateTimeOffset.UtcNow, actorId, actorId).Value;
        _saleInventoryMutator.MutateAsync(shopId, Arg.Any<string>(), line, actorId, Arg.Any<CancellationToken>())
            .Returns(new MutatedSaleLine(saleItem, tx, 18m));

        _saleAggregator.AggregateAsync(
                Arg.Any<string>(), shopId, Arg.Any<decimal>(), Arg.Any<decimal>(),
                actorId, Arg.Any<Customer?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                PaymentMethod.Cash, Arg.Any<IReadOnlyList<MutatedSaleLine>>(), Arg.Any<List<string>>(),
                Arg.Any<IReadOnlyDictionary<Guid, string>>(), Arg.Any<CancellationToken>())
            .Returns(Errors.Sale.PaidAndDueAmountMismatch);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.PaidAndDueAmountMismatch.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
