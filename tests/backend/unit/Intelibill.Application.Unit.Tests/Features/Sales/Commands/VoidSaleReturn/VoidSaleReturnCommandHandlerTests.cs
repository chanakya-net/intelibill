using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.VoidSaleReturn;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Commands.VoidSaleReturn;

public sealed class VoidSaleReturnCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly ISaleReturnRepository _saleReturnRepository = Substitute.For<ISaleReturnRepository>();
    private readonly IInventoryRepository _inventoryRepository = Substitute.For<IInventoryRepository>();
    private readonly IInventoryBatchRepository _inventoryBatchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly IStockTransactionRepository _stockTransactionRepository = Substitute.For<IStockTransactionRepository>();
    private readonly ICustomerLedgerEntryRepository _customerLedgerEntryRepository = Substitute.For<ICustomerLedgerEntryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private VoidSaleReturnCommandHandler CreateHandler() =>
        new(
            _userRepository,
            _shopRepository,
            _saleRepository,
            _saleReturnRepository,
            _inventoryRepository,
            _inventoryBatchRepository,
            _stockTransactionRepository,
            _customerLedgerEntryRepository,
            _unitOfWork);

    [Theory]
    [InlineData(ShopRole.Owner)]
    [InlineData(ShopRole.Manager)]
    public async Task HandleAsync_ForOwnerOrManager_VoidsReturnAndCreatesCompensatingEffects(ShopRole role)
    {
        var fixture = Arrange(role, returnDueReductionAmount: 75m);

        var result = await CreateHandler().HandleAsync(
            new VoidSaleReturnCommand(fixture.User.Id, fixture.Shop.Id, fixture.SaleReturn.Id, "Duplicate return"),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(fixture.SaleReturn.IsVoided);
        Assert.Equal("Duplicate return", fixture.SaleReturn.VoidReason);
        Assert.Equal(8m, fixture.Batch.Quantity);
        Assert.Equal(18m, fixture.Inventory.Quantity);
        await _stockTransactionRepository.Received(1).AddAsync(
            Arg.Is<StockTransaction>(tx =>
                tx.TransactionType == StockTransactionType.Out
                && tx.Quantity == -2m
                && tx.ReferenceNumber == fixture.SaleReturn.ReturnNumber),
            Arg.Any<CancellationToken>());
        await _customerLedgerEntryRepository.Received(1).AddAsync(
            Arg.Is<CustomerLedgerEntry>(entry =>
                entry.EntryType == CustomerLedgerEntryType.ReturnCreditReversal
                && entry.Amount == 75m
                && entry.SaleId == fixture.Sale.Id
                && entry.CustomerId == fixture.Sale.CustomerId),
            Arg.Any<CancellationToken>());
        _saleReturnRepository.Received(1).Update(fixture.SaleReturn);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenAlreadyVoided_ReturnsValidationError()
    {
        var fixture = Arrange(ShopRole.Owner);
        fixture.SaleReturn.Void(DateTimeOffset.UtcNow, fixture.User.Id, "Earlier void");

        var result = await CreateHandler().HandleAsync(
            new VoidSaleReturnCommand(fixture.User.Id, fixture.Shop.Id, fixture.SaleReturn.Id, "Duplicate return"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.ReturnAlreadyVoided.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenReasonMissing_ReturnsValidationError()
    {
        var fixture = Arrange(ShopRole.Owner);

        var result = await CreateHandler().HandleAsync(
            new VoidSaleReturnCommand(fixture.User.Id, fixture.Shop.Id, fixture.SaleReturn.Id, " "),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.ReturnVoidReasonRequired.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenCompensationWouldMakeStockNegative_ReturnsConflict()
    {
        var fixture = Arrange(ShopRole.Owner, batchQuantity: 1m, inventoryQuantity: 1m);

        var result = await CreateHandler().HandleAsync(
            new VoidSaleReturnCommand(fixture.User.Id, fixture.Shop.Id, fixture.SaleReturn.Id, "Duplicate return"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.ReturnVoidInsufficientStock.Code, result.FirstError.Code);
        Assert.False(fixture.SaleReturn.IsVoided);
        await _stockTransactionRepository.DidNotReceive().AddAsync(
            Arg.Any<StockTransaction>(),
            Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ForWastageOnlyReturn_DoesNotCreateStockOrLedgerCompensation()
    {
        var fixture = Arrange(
            ShopRole.Owner,
            condition: SaleReturnCondition.Wastage,
            returnDueReductionAmount: 0m);

        var result = await CreateHandler().HandleAsync(
            new VoidSaleReturnCommand(fixture.User.Id, fixture.Shop.Id, fixture.SaleReturn.Id, "Wrong return"),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(fixture.SaleReturn.IsVoided);
        Assert.Equal(10m, fixture.Batch.Quantity);
        Assert.Equal(20m, fixture.Inventory.Quantity);
        await _stockTransactionRepository.DidNotReceive().AddAsync(
            Arg.Any<StockTransaction>(),
            Arg.Any<CancellationToken>());
        await _customerLedgerEntryRepository.DidNotReceive().AddAsync(
            Arg.Any<CustomerLedgerEntry>(),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private VoidSaleReturnFixture Arrange(
        ShopRole role,
        SaleReturnCondition condition = SaleReturnCondition.Restockable,
        decimal returnDueReductionAmount = 0m,
        decimal batchQuantity = 10m,
        decimal inventoryQuantity = 20m)
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Shop", "Address", "City", "State", "560001", null, null, null);
        var customerId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var batch = InventoryBatch.Create(
            shop.Id,
            itemId,
            "B-001",
            batchQuantity,
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
            inventoryQuantity,
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
            customerId,
            "Customer",
            null,
            PaymentMethod.Credit,
            DateTimeOffset.UtcNow,
            paidAmount: 300m,
            dueAmount: 250m,
            totalAmount: 550m,
            totalTaxAmount: 50m,
            [saleItem]);
        var returnItem = SaleReturnItem.Create(
            shop.Id,
            sale.Id,
            saleItem.Id,
            quantity: 2m,
            condition,
            originalCostPrice: 80m,
            originalSalesPrice: 100m,
            originalTaxRatePercent: 10m,
            originalIsPriceIncludingTax: false,
            maxRefundAmount: 220m,
            approvedRefundAmount: 220m,
            taxableAmount: 200m,
            taxAmount: 20m,
            notes: condition == SaleReturnCondition.Wastage ? "Damaged" : "Sealed").Value;
        var saleReturnLine = new SaleReturnLineInput(
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

        var saleReturn = SaleReturn.Record(
            shop.Id,
            sale.Id,
            "RET-20260505-VOID01",
            DateTimeOffset.UtcNow,
            user.Id,
            notes: null,
            totalRefundAmount: 220m,
            dueReductionAmount: returnDueReductionAmount,
            payoutAmount: 220m - returnDueReductionAmount,
            payoutMethod: PaymentMethod.Cash,
            totalTaxableAmount: 200m,
            totalTaxAmount: 20m,
            customerBalanceBefore: 250m,
            customerBalanceAfter: 250m - returnDueReductionAmount,
            [saleReturnLine]).Value;

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shop.Id, user.Id, role, true));
        _saleReturnRepository.GetByIdWithItemsAsync(shop.Id, saleReturn.Id, Arg.Any<CancellationToken>())
            .Returns(saleReturn);
        _saleRepository.GetByIdAsync(sale.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(sale);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>()).Returns(inventory);

        return new VoidSaleReturnFixture(user, shop, sale, saleReturn, batch, inventory);
    }

    private sealed record VoidSaleReturnFixture(
        User User,
        Shop Shop,
        Sale Sale,
        SaleReturn SaleReturn,
        InventoryBatch Batch,
        Intelibill.Domain.Entities.Inventory Inventory);
}
