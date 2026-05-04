using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.RecordSaleReturn;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Application.Features.Sales.Services.Returns;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
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
    private readonly ISaleReturnNumberGenerator _returnNumberGenerator = Substitute.For<ISaleReturnNumberGenerator>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ISaleReturnCalculator _calculator = new SaleReturnCalculator();

    private RecordSaleReturnCommandHandler CreateHandler() =>
        new(
            new SaleReturnValidator(
                _userRepository,
                _shopRepository,
                _saleRepository,
                _saleReturnRepository,
                _inventoryBatchRepository,
                _calculator),
            _returnNumberGenerator,
            _inventoryRepository,
            _inventoryBatchRepository,
            _stockTransactionRepository,
            _saleReturnRepository,
            _unitOfWork);

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
        var secondSaleItem = SaleItem.Create(
            fixture.Shop.Id,
            secondItemId,
            secondBatch.Id,
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
                    new RecordSaleReturnItemCommand(fixture.SaleItem.Id, 2m, SaleReturnCondition.Restockable, ApprovedRefundAmount: null, Notes: "Sealed"),
                    new RecordSaleReturnItemCommand(secondSaleItem.Id, 1m, SaleReturnCondition.Wastage, ApprovedRefundAmount: 25m, Notes: "Damaged"),
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

    [Theory]
    [InlineData(SaleReturnCondition.Wastage, null, "SaleReturn.NoteRequired")]
    [InlineData(SaleReturnCondition.Restockable, 50.0, "SaleReturn.NoteRequired")]
    [InlineData(SaleReturnCondition.Restockable, 0.0, "SaleReturn.NoteRequired")]
    public async Task HandleAsync_WhenLineRequiresNoteAndNoteIsMissing_ReturnsValidationError(
        SaleReturnCondition condition,
        double? approvedRefundAmount,
        string expectedCode)
    {
        var fixture = ArrangeSale(ShopRole.Owner);

        var result = await CreateHandler().HandleAsync(
            Command(
                fixture.User.Id,
                fixture.Shop.Id,
                fixture.Sale.Id,
                fixture.SaleItem.Id,
                payoutMethod: PaymentMethod.Cash,
                condition,
                approvedRefundAmount.HasValue ? (decimal)approvedRefundAmount.Value : null,
                lineNotes: null),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, error => error.Code == expectedCode);
        Assert.Equal(10m, fixture.Batch.Quantity);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenPayoutIsPositiveAndMethodMissing_ReturnsValidationError()
    {
        var fixture = ArrangeSale(ShopRole.Owner);

        var result = await CreateHandler().HandleAsync(
            Command(fixture.User.Id, fixture.Shop.Id, fixture.Sale.Id, fixture.SaleItem.Id, payoutMethod: null),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.ReturnPayoutMethodRequired.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenPayoutMethodIsCredit_ReturnsValidationError()
    {
        var fixture = ArrangeSale(ShopRole.Owner);

        var result = await CreateHandler().HandleAsync(
            Command(fixture.User.Id, fixture.Shop.Id, fixture.Sale.Id, fixture.SaleItem.Id, payoutMethod: PaymentMethod.Credit),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.ReturnPayoutMethodInvalid.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenReturnWouldReduceDue_ReturnsValidationErrorUntilCustomerLedgerIsImplemented()
    {
        var fixture = ArrangeSale(ShopRole.Owner, dueAmount: 300m);

        var result = await CreateHandler().HandleAsync(
            Command(fixture.User.Id, fixture.Shop.Id, fixture.Sale.Id, fixture.SaleItem.Id, payoutMethod: PaymentMethod.Cash),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.ReturnCustomerDueNotSupported.Code, result.FirstError.Code);
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

    private SaleReturnFixture ArrangeSale(ShopRole role, decimal dueAmount = 0m)
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Shop", "Address", "City", "State", "560001", null, null, null);
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
        var saleItem = SaleItem.Create(
            shop.Id,
            itemId,
            batch.Id,
            quantity: 5m,
            costPrice: 80m,
            salesPrice: 100m,
            mrp: 120m,
            taxRatePercent: 10m,
            isPriceIncludingTax: false,
            hasPriceMismatch: false);
        var sale = Sale.Create(
            shop.Id,
            "INV-001",
            customerId: null,
            customerName: null,
            customerPhone: null,
            paymentMethod: PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            paidAmount: 550m - dueAmount,
            dueAmount,
            totalAmount: 550m,
            totalTaxAmount: 50m,
            [saleItem]);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shop.Id, user.Id, role, true));
        _saleRepository.GetByIdAsync(sale.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(sale);
        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>()).Returns([]);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>()).Returns(inventory);

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
        string? lineNotes = "Sealed") =>
        new(
            userId,
            shopId,
            saleId,
            payoutMethod,
            DueReductionOverrideAmount: null,
            DueOverrideReason: null,
            Notes: "Customer returned sealed items",
            [new RecordSaleReturnItemCommand(saleItemId, 2m, condition, approvedRefundAmount, lineNotes)]);

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

        return SaleReturn.Create(
            shopId,
            saleId,
            $"RET-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            notes: null,
            totalRefundAmount: quantity * 110m,
            dueReductionAmount: 0m,
            payoutAmount: quantity * 110m,
            totalTaxableAmount: quantity * 100m,
            totalTaxAmount: quantity * 10m,
            customerBalanceBefore: null,
            customerBalanceAfter: null,
            [returnItem]).Value;
    }

    private sealed record SaleReturnFixture(
        User User,
        Shop Shop,
        Sale Sale,
        SaleItem SaleItem,
        InventoryBatch Batch,
        Intelibill.Domain.Entities.Inventory Inventory);
}
