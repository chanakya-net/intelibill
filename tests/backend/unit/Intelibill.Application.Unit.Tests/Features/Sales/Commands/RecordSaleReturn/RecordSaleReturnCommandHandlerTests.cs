using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.RecordSaleReturn;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Application.Features.Sales.Services.Returns;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Commands.RecordSaleReturn;

public sealed class RecordSaleReturnCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly ISaleReturnRepository _saleReturnRepository = Substitute.For<ISaleReturnRepository>();
    private readonly IInventoryBatchRepository _inventoryBatchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly IInventoryRepository _inventoryRepository = Substitute.For<IInventoryRepository>();
    private readonly IStockTransactionRepository _stockTransactionRepository = Substitute.For<IStockTransactionRepository>();
    private readonly ICustomerLedgerEntryRepository _customerLedgerEntryRepository = Substitute.For<ICustomerLedgerEntryRepository>();
    private readonly ISaleReturnNumberGenerator _returnNumberGenerator = Substitute.For<ISaleReturnNumberGenerator>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ISaleReturnCalculator _calculator = new SaleReturnCalculator();

    public RecordSaleReturnCommandHandlerTests()
    {
        _returnNumberGenerator.Generate(Arg.Any<DateTimeOffset>()).Returns("RET-TEST-DEFAULT");
    }

    private RecordSaleReturnCommandHandler CreateHandler()
    {
        return new(
            new SaleReturnValidator(
                _userRepository,
                _shopRepository,
                _saleRepository,
                _saleReturnRepository,
                _inventoryBatchRepository,
                _customerLedgerEntryRepository,
                _calculator),
            _returnNumberGenerator,
            _inventoryRepository,
            _inventoryBatchRepository,
            _stockTransactionRepository,
            _saleReturnRepository,
            _customerLedgerEntryRepository,
            _unitOfWork);
    }

    [Theory]
    [InlineData(ShopRole.Owner)]
    [InlineData(ShopRole.Manager)]
    public async Task HandleAsync_ForOwnerOrManager_RestocksAndRecordsReturnAtomically(ShopRole role)
    {
        var fixture = ArrangeSale(role);
        _returnNumberGenerator.Generate(Arg.Any<DateTimeOffset>()).Returns("RET-20260505-ABC123EF");

        var result = await CreateHandler().HandleAsync(
            Command(fixture.User.Id, fixture.Shop.Id, fixture.Sale.Id, fixture.SaleItem.Id, payoutMethod: PaymentMethod.Cash),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(12m, fixture.Batch.Quantity);
        Assert.Equal(22m, fixture.Inventory.Quantity);

        await _saleReturnRepository.Received(1).AddAsync(
            Arg.Is<SaleReturn>(r =>
                r.ReturnNumber == "RET-20260505-ABC123EF"
                && r.PayoutAmount == 220m
                && r.Items.Count == 1
                && r.Items[0].Condition == SaleReturnCondition.Restockable),
            Arg.Any<CancellationToken>());
        await _stockTransactionRepository.Received(1).AddAsync(
            Arg.Is<StockTransaction>(t =>
                t.TransactionType == StockTransactionType.Ret
                && t.Quantity == 2m
                && t.ReferenceNumber == "RET-20260505-ABC123EF"),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenRefundExceedsDiscountedMaxWithoutReason_ReturnsValidationError()
    {
        var fixture = ArrangeSale(ShopRole.Owner, itemDiscountAmount: 50m, saleDiscountAmount: 25m);

        var result = await CreateHandler().HandleAsync(
            Command(
                fixture.User.Id,
                fixture.Shop.Id,
                fixture.Sale.Id,
                fixture.SaleItem.Id,
                payoutMethod: PaymentMethod.Cash,
                approvedRefundAmount: 200m,
                lineNotes: null),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("SaleReturn.RefundOverrideReasonRequired", result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenRefundExceedsDiscountedMaxWithReason_RecordsReturn()
    {
        var fixture = ArrangeSale(ShopRole.Owner, itemDiscountAmount: 50m, saleDiscountAmount: 25m);
        _returnNumberGenerator.Generate(Arg.Any<DateTimeOffset>()).Returns("RET-20260505-OVERRIDE01");

        var result = await CreateHandler().HandleAsync(
            Command(
                fixture.User.Id,
                fixture.Shop.Id,
                fixture.Sale.Id,
                fixture.SaleItem.Id,
                payoutMethod: PaymentMethod.Cash,
                approvedRefundAmount: 200m,
                lineNotes: "Goodwill"),
            CancellationToken.None);

        Assert.False(result.IsError, string.Join(", ", result.Errors.Select(e => e.Code)));
        await _saleReturnRepository.Received(1).AddAsync(
            Arg.Is<SaleReturn>(r =>
                r.ReturnNumber == "RET-20260505-OVERRIDE01"
                && r.Items.Count == 1
                && r.Items[0].SaleItemId == fixture.SaleItem.Id
                && r.Items[0].ApprovedRefundAmount == 200m
                && r.Items[0].Notes == "Goodwill"),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ForStaff_ReturnsForbiddenAndDoesNotSave()
    {
        var fixture = ArrangeSale(ShopRole.Staff);

        var result = await CreateHandler().HandleAsync(
            Command(fixture.User.Id, fixture.Shop.Id, fixture.Sale.Id, fixture.SaleItem.Id, payoutMethod: PaymentMethod.Cash),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.ReturnForbidden.Code, result.FirstError.Code);
        Assert.Equal(10m, fixture.Batch.Quantity);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ForWastageReturn_RecordsReturnWithoutRestockingOrStockTransaction()
    {
        var fixture = ArrangeSale(ShopRole.Owner);
        _returnNumberGenerator.Generate(Arg.Any<DateTimeOffset>()).Returns("RET-20260505-WASTE001");

        var result = await CreateHandler().HandleAsync(
            Command(
                fixture.User.Id,
                fixture.Shop.Id,
                fixture.Sale.Id,
                fixture.SaleItem.Id,
                payoutMethod: PaymentMethod.Cash,
                condition: SaleReturnCondition.Wastage,
                approvedRefundAmount: 50m,
                lineNotes: "Damaged after sale"),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(10m, fixture.Batch.Quantity);
        Assert.Equal(20m, fixture.Inventory.Quantity);
        _inventoryBatchRepository.DidNotReceive().Update(Arg.Any<InventoryBatch>());
        _inventoryRepository.DidNotReceive().Update(Arg.Any<Intelibill.Domain.Entities.Inventory>());
        await _stockTransactionRepository.DidNotReceive().AddAsync(
            Arg.Any<StockTransaction>(),
            Arg.Any<CancellationToken>());
        await _saleReturnRepository.Received(1).AddAsync(
            Arg.Is<SaleReturn>(r =>
                r.ReturnNumber == "RET-20260505-WASTE001"
                && r.TotalRefundAmount == 50m
                && r.Items.Count == 1
                && r.Items[0].Condition == SaleReturnCondition.Wastage
                && r.Items[0].OriginalCostPrice == 80m),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ForServiceRefundOnly_RecordsReturnWithoutInventoryOrStockMovement()
    {
        var fixture = ArrangeSale(ShopRole.Owner);
        var serviceItem = SaleItem.CreateService(
            fixture.Shop.Id,
            Guid.NewGuid(),
            lineName: "Consultation",
            lineCode: "SRV-001",
            quantity: 2m,
            costPrice: 0m,
            salesPrice: 150m,
            mrp: 0m,
            taxRatePercent: 0m,
            isPriceIncludingTax: true,
            hasPriceMismatch: false);
        var sale = Sale.Create(
            fixture.Shop.Id,
            "INV-SRV-001",
            customerId: null,
            customerName: null,
            customerPhone: null,
            paymentMethod: PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            paidAmount: 300m,
            dueAmount: 0m,
            totalAmount: 300m,
            totalTaxAmount: 0m,
            [serviceItem]);
        _saleRepository.GetByIdAsync(sale.Id, fixture.Shop.Id, Arg.Any<CancellationToken>()).Returns(sale);
        _saleReturnRepository.GetBySaleAsync(fixture.Shop.Id, sale.Id, Arg.Any<CancellationToken>()).Returns([]);
        _returnNumberGenerator.Generate(Arg.Any<DateTimeOffset>()).Returns("RET-20260505-SERVICE1");

        var result = await CreateHandler().HandleAsync(
            new RecordSaleReturnCommand(
                fixture.User.Id,
                fixture.Shop.Id,
                sale.Id,
                PaymentMethod.Cash,
                DueReductionOverrideAmount: null,
                DueOverrideReason: null,
                Notes: "Service refund",
                [new RecordSaleReturnItemCommand(serviceItem.Id, 1m, SaleLineType.Service, SaleReturnCondition.Wastage, ApprovedRefundAmount: null, Notes: "Not delivered")] ),
            CancellationToken.None);

        Assert.False(result.IsError);
        _inventoryBatchRepository.DidNotReceive().Update(Arg.Any<InventoryBatch>());
        _inventoryRepository.DidNotReceive().Update(Arg.Any<Intelibill.Domain.Entities.Inventory>());
        await _stockTransactionRepository.DidNotReceive().AddAsync(Arg.Any<StockTransaction>(), Arg.Any<CancellationToken>());
        await _saleReturnRepository.Received(1).AddAsync(
            Arg.Is<SaleReturn>(r =>
                r.ReturnNumber == "RET-20260505-SERVICE1"
                && r.Items.Count == 1
                && r.Items[0].SaleItemId == serviceItem.Id
                && r.Items[0].OriginalCostPrice == 0m
                && r.TotalRefundAmount == 150m
                && r.PayoutAmount == 150m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ForMixedMultiLineReturn_RecordsOneHeaderWithIndependentLineEffects()
    {
        var fixture = ArrangeSale(ShopRole.Owner);
        var secondItemId = Guid.NewGuid();
        var secondBatch = InventoryBatch.Create(
            fixture.Shop.Id,
            secondItemId,
            "B-002",
            quantity: 3m,
            costPrice: 35m,
            mrp: 60m,
            salesPrice: 50m,
            taxRatePercent: 0m,
            taxIncluded: true,
            expiryDate: null,
            manufacturingDate: null,
            supplierId: null,
            fixture.User.Id).Value;
        var secondSaleItem = SaleItem.CreateGoods(
            fixture.Shop.Id,
            secondItemId,
            secondBatch.Id,
            lineName: "Item",
            lineCode: "BC-001",
            quantity: 2m,
            costPrice: 35m,
            salesPrice: 50m,
            mrp: 60m,
            taxRatePercent: 0m,
            isPriceIncludingTax: true,
            hasPriceMismatch: false);
        var sale = Sale.Create(
            fixture.Shop.Id,
            "INV-002",
            customerId: null,
            customerName: null,
            customerPhone: null,
            paymentMethod: PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            paidAmount: 650m,
            dueAmount: 0m,
            totalAmount: 650m,
            totalTaxAmount: 50m,
            [fixture.SaleItem, secondSaleItem]);

        _saleRepository.GetByIdAsync(sale.Id, fixture.Shop.Id, Arg.Any<CancellationToken>()).Returns(sale);
        _saleReturnRepository.GetBySaleAsync(fixture.Shop.Id, sale.Id, Arg.Any<CancellationToken>()).Returns([]);
        _inventoryBatchRepository.GetByIdAsync(secondBatch.Id, Arg.Any<CancellationToken>()).Returns(secondBatch);
        _returnNumberGenerator.Generate(Arg.Any<DateTimeOffset>()).Returns("RET-20260505-MIXED01");

        var result = await CreateHandler().HandleAsync(
            new RecordSaleReturnCommand(
                fixture.User.Id,
                fixture.Shop.Id,
                sale.Id,
                PaymentMethod.Cash,
                DueReductionOverrideAmount: null,
                DueOverrideReason: null,
                Notes: "Mixed return",
                [
                    new RecordSaleReturnItemCommand(fixture.SaleItem.Id, 2m, SaleLineType.Goods, SaleReturnCondition.Restockable, ApprovedRefundAmount: null, Notes: "Sealed"),
                    new RecordSaleReturnItemCommand(secondSaleItem.Id, 1m, SaleLineType.Goods, SaleReturnCondition.Wastage, ApprovedRefundAmount: 25m, Notes: "Damaged"),
                ]),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(12m, fixture.Batch.Quantity);
        Assert.Equal(22m, fixture.Inventory.Quantity);
        Assert.Equal(3m, secondBatch.Quantity);
        await _stockTransactionRepository.Received(1).AddAsync(
            Arg.Is<StockTransaction>(t =>
                t.TransactionType == StockTransactionType.Ret
                && t.ItemId == fixture.SaleItem.ItemId
                && t.Quantity == 2m
                && t.ReferenceNumber == "RET-20260505-MIXED01"),
            Arg.Any<CancellationToken>());
        await _saleReturnRepository.Received(1).AddAsync(
            Arg.Is<SaleReturn>(r =>
                r.ReturnNumber == "RET-20260505-MIXED01"
                && r.Items.Count == 2
                && r.TotalRefundAmount == 245m
                && r.PayoutAmount == 245m
                && r.TotalTaxableAmount == 250m
                && r.TotalTaxAmount == 20m
                && r.Items.Any(i => i.SaleItemId == fixture.SaleItem.Id && i.Condition == SaleReturnCondition.Restockable && i.ApprovedRefundAmount == 220m)
                && r.Items.Any(i => i.SaleItemId == secondSaleItem.Id && i.Condition == SaleReturnCondition.Wastage && i.ApprovedRefundAmount == 25m)),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenCustomerHasOutstandingDue_CreatesReturnCreditBeforePayout()
    {
        var fixture = ArrangeSale(ShopRole.Owner, dueAmount: 300m, hasCustomer: true);
        _customerLedgerEntryRepository.GetCustomerBalanceAsync(fixture.Shop.Id, fixture.Sale.CustomerId!.Value, Arg.Any<CancellationToken>())
            .Returns(100m);
        _returnNumberGenerator.Generate(Arg.Any<DateTimeOffset>()).Returns("RET-20260505-DUE001");

        var result = await CreateHandler().HandleAsync(
            Command(fixture.User.Id, fixture.Shop.Id, fixture.Sale.Id, fixture.SaleItem.Id, payoutMethod: PaymentMethod.Cash),
            CancellationToken.None);

        Assert.False(result.IsError);
        await _customerLedgerEntryRepository.Received(1).AddAsync(
            Arg.Is<CustomerLedgerEntry>(entry =>
                entry.CustomerId == fixture.Sale.CustomerId
                && entry.SaleId == fixture.Sale.Id
                && entry.EntryType == CustomerLedgerEntryType.ReturnCredit
                && entry.Amount == 100m),
            Arg.Any<CancellationToken>());
        await _saleReturnRepository.Received(1).AddAsync(
            Arg.Is<SaleReturn>(r =>
                r.DueReductionAmount == 100m
                && r.PayoutAmount == 120m
                && r.CustomerBalanceBefore == 100m
                && r.CustomerBalanceAfter == 0m),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenCustomerHasNoOutstandingDue_PaysOutWithoutReturnCredit()
    {
        var fixture = ArrangeSale(ShopRole.Owner, dueAmount: 0m, hasCustomer: true);
        _customerLedgerEntryRepository.GetCustomerBalanceAsync(fixture.Shop.Id, fixture.Sale.CustomerId!.Value, Arg.Any<CancellationToken>())
            .Returns(0m);
        _returnNumberGenerator.Generate(Arg.Any<DateTimeOffset>()).Returns("RET-20260505-PAID001");

        var result = await CreateHandler().HandleAsync(
            Command(fixture.User.Id, fixture.Shop.Id, fixture.Sale.Id, fixture.SaleItem.Id, payoutMethod: PaymentMethod.Cash),
            CancellationToken.None);

        Assert.False(result.IsError);
        await _customerLedgerEntryRepository.DidNotReceive().AddAsync(
            Arg.Any<CustomerLedgerEntry>(),
            Arg.Any<CancellationToken>());
        await _saleReturnRepository.Received(1).AddAsync(
            Arg.Is<SaleReturn>(r =>
                r.DueReductionAmount == 0m
                && r.PayoutAmount == 220m
                && r.CustomerBalanceBefore == 0m
                && r.CustomerBalanceAfter == 0m),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenDueOverridePaysOutWhileDueRemainsWithoutReason_ReturnsValidationError()
    {
        var fixture = ArrangeSale(ShopRole.Owner, dueAmount: 300m, hasCustomer: true);

        var result = await CreateHandler().HandleAsync(
            Command(
                fixture.User.Id,
                fixture.Shop.Id,
                fixture.Sale.Id,
                fixture.SaleItem.Id,
                payoutMethod: PaymentMethod.Cash,
                dueReductionOverrideAmount: 25m,
                dueOverrideReason: null),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.ReturnDueOverrideReasonRequired.Code, result.FirstError.Code);
        await _customerLedgerEntryRepository.DidNotReceive().AddAsync(
            Arg.Any<CustomerLedgerEntry>(),
            Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenDueOverridePaysOutWhileDueRemainsWithReason_RecordsSplit()
    {
        var fixture = ArrangeSale(ShopRole.Owner, dueAmount: 300m, hasCustomer: true);
        _returnNumberGenerator.Generate(Arg.Any<DateTimeOffset>()).Returns("RET-20260505-SPLIT01");

        var result = await CreateHandler().HandleAsync(
            Command(
                fixture.User.Id,
                fixture.Shop.Id,
                fixture.Sale.Id,
                fixture.SaleItem.Id,
                payoutMethod: PaymentMethod.Cash,
                dueReductionOverrideAmount: 25m,
                dueOverrideReason: "Customer needs cash refund"),
            CancellationToken.None);

        Assert.False(result.IsError);
        await _customerLedgerEntryRepository.Received(1).AddAsync(
            Arg.Is<CustomerLedgerEntry>(entry =>
                entry.EntryType == CustomerLedgerEntryType.ReturnCredit
                && entry.Amount == 25m),
            Arg.Any<CancellationToken>());
        await _saleReturnRepository.Received(1).AddAsync(
            Arg.Is<SaleReturn>(r =>
                r.DueReductionAmount == 25m
                && r.PayoutAmount == 195m
                && r.CustomerBalanceBefore == 300m
                && r.CustomerBalanceAfter == 275m),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenDueOverrideExceedsOutstandingDue_ReturnsValidationError()
    {
        var fixture = ArrangeSale(ShopRole.Owner, dueAmount: 100m, hasCustomer: true);

        var result = await CreateHandler().HandleAsync(
            Command(
                fixture.User.Id,
                fixture.Shop.Id,
                fixture.Sale.Id,
                fixture.SaleItem.Id,
                payoutMethod: PaymentMethod.Cash,
                dueReductionOverrideAmount: 150m,
                dueOverrideReason: "Too much"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.ReturnDueReductionExceedsOutstandingDue.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenQuantityExceedsCurrentRemaining_ReturnsValidationError()
    {
        var fixture = ArrangeSale(ShopRole.Owner);
        var previousReturn = MakeSaleReturn(fixture.Shop.Id, fixture.Sale.Id, fixture.SaleItem.Id, quantity: 4m);
        _saleReturnRepository.GetBySaleAsync(fixture.Shop.Id, fixture.Sale.Id, Arg.Any<CancellationToken>())
            .Returns([previousReturn]);

        var result = await CreateHandler().HandleAsync(
            Command(fixture.User.Id, fixture.Shop.Id, fixture.Sale.Id, fixture.SaleItem.Id, payoutMethod: PaymentMethod.Cash),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("SaleReturn.QuantityExceedsRemaining", result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private SaleReturnFixture ArrangeSale(
        ShopRole role,
        decimal dueAmount = 0m,
        bool hasCustomer = false,
        decimal itemDiscountAmount = 0m,
        decimal saleDiscountAmount = 0m)
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Shop", "Address", "City", "State", "560001", null, null, null);
        var customerId = hasCustomer ? Guid.NewGuid() : (Guid?)null;
        var itemId = Guid.NewGuid();
        var batch = InventoryBatch.Create(
            shop.Id,
            itemId,
            "B-001",
            quantity: 10m,
            costPrice: 80m,
            mrp: 120m,
            salesPrice: 100m,
            taxRatePercent: 10m,
            taxIncluded: false,
            expiryDate: null,
            manufacturingDate: null,
            supplierId: null,
            user.Id).Value;
        var inventory = Intelibill.Domain.Entities.Inventory.Create(
            shop.Id,
            itemId,
            quantity: 20m,
            reorderLevel: 0m,
            maxLevel: 100m,
            createdBy: user.Id).Value;
        var saleItem = SaleItem.CreateGoods(
            shop.Id,
            itemId,
            batch.Id,
            lineName: "Item",
            lineCode: "BC-001",
            quantity: 5m,
            costPrice: 80m,
            salesPrice: 100m,
            mrp: 120m,
            taxRatePercent: 10m,
            isPriceIncludingTax: false,
            hasPriceMismatch: false,
            itemDiscountAmount: itemDiscountAmount,
            saleDiscountAmount: saleDiscountAmount);

        var totalAmount = saleItem.TotalAmount;
        var totalTaxAmount = saleItem.TaxAmount;

        var sale = Sale.Create(
            shop.Id,
            "INV-001",
            customerId,
            customerName: hasCustomer ? "Customer" : null,
            customerPhone: null,
            paymentMethod: PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            paidAmount: totalAmount - dueAmount,
            dueAmount,
            totalAmount: totalAmount,
            totalTaxAmount: totalTaxAmount,
            [saleItem]);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shop.Id, user.Id, role, true));
        _saleRepository.GetByIdAsync(sale.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(sale);
        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>()).Returns([]);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>()).Returns(inventory);
        if (sale.CustomerId.HasValue)
        {
            _customerLedgerEntryRepository.GetCustomerBalanceAsync(shop.Id, sale.CustomerId.Value, Arg.Any<CancellationToken>())
                .Returns(dueAmount);
        }

        return new SaleReturnFixture(user, shop, sale, saleItem, batch, inventory);
    }

    private static RecordSaleReturnCommand Command(
        Guid userId,
        Guid shopId,
        Guid saleId,
        Guid saleItemId,
        PaymentMethod? payoutMethod,
        SaleReturnCondition condition = SaleReturnCondition.Restockable,
        decimal? approvedRefundAmount = null,
        string? lineNotes = "Sealed",
        decimal? dueReductionOverrideAmount = null,
        string? dueOverrideReason = null) =>
        new(
            userId,
            shopId,
            saleId,
            payoutMethod,
            dueReductionOverrideAmount,
            dueOverrideReason,
            Notes: "Customer returned sealed items",
            [new RecordSaleReturnItemCommand(saleItemId, 2m, SaleLineType.Goods, condition, approvedRefundAmount, lineNotes)]);

    private static SaleReturn MakeSaleReturn(Guid shopId, Guid saleId, Guid saleItemId, decimal quantity)
    {
        var returnItem = SaleReturnItem.Create(
            shopId,
            saleId,
            saleItemId,
            quantity,
            SaleReturnCondition.Restockable,
            originalCostPrice: 80m,
            originalSalesPrice: 100m,
            originalTaxRatePercent: 10m,
            originalIsPriceIncludingTax: false,
            maxRefundAmount: quantity * 110m,
            approvedRefundAmount: quantity * 110m,
            taxableAmount: quantity * 100m,
            taxAmount: quantity * 10m,
            notes: null).Value;

        var returnLine = new SaleReturnLineInput(
            returnItem.ShopId,
            returnItem.SaleItemId,
            returnItem.Quantity,
            returnItem.Condition,
            returnItem.OriginalCostPrice,
            returnItem.OriginalSalesPrice,
            returnItem.OriginalTaxRatePercent,
            returnItem.OriginalIsPriceIncludingTax,
            returnItem.MaxRefundAmount,
            returnItem.ApprovedRefundAmount,
            returnItem.TaxableAmount,
            returnItem.TaxAmount,
            returnItem.Notes);

        return SaleReturn.Record(
            shopId,
            saleId,
            $"RET-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            notes: null,
            totalRefundAmount: quantity * 110m,
            dueReductionAmount: 0m,
            payoutAmount: quantity * 110m,
            payoutMethod: PaymentMethod.Cash,
            totalTaxableAmount: quantity * 100m,
            totalTaxAmount: quantity * 10m,
            customerBalanceBefore: null,
            customerBalanceAfter: null,
            [returnLine]).Value;
    }

    private sealed record SaleReturnFixture(
        User User,
        Shop Shop,
        Sale Sale,
        SaleItem SaleItem,
        InventoryBatch Batch,
        Intelibill.Domain.Entities.Inventory Inventory);
}
