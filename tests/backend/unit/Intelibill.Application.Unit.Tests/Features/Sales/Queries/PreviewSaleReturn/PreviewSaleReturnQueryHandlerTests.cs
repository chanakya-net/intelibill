using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Queries.PreviewSaleReturn;
using Intelibill.Application.Features.Sales.Services.Returns;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Queries.PreviewSaleReturn;

public class PreviewSaleReturnQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly ISaleReturnRepository _saleReturnRepository = Substitute.For<ISaleReturnRepository>();
    private readonly IInventoryBatchRepository _inventoryBatchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly ISaleReturnCalculator _calculator = new SaleReturnCalculator();

    private PreviewSaleReturnQueryHandler CreateHandler() =>
        new(_userRepository, _shopRepository, _saleRepository, _saleReturnRepository, _inventoryBatchRepository, _calculator);

    [Theory]
    [InlineData(ShopRole.Owner)]
    [InlineData(ShopRole.Manager)]
    public async Task Handle_SaleReturnPreview_ForOwnerOrManager_ReturnsFullFinancialPreview(ShopRole role)
    {
        var fixture = ArrangeSale(role);
        var query = Query(fixture.User.Id, fixture.Shop.Id, fixture.Sale.Id, fixture.SaleItem.Id, quantity: 2m);

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(result.Value.HasFinancialAccess);
        Assert.NotNull(result.Value.Financial);
        Assert.NotNull(result.Value.Lines[0].Financial);
        Assert.Equal(220m, result.Value.Financial!.TotalRefundAmount);
        Assert.Equal(2m, result.Value.Lines[0].RequestedQuantity);
        Assert.Equal(5m, result.Value.Lines[0].ReturnableQuantity);
    }

    [Fact]
    public async Task Handle_SaleReturnPreview_ForStaff_ReturnsOperationalOnlyPreview()
    {
        var fixture = ArrangeSale(ShopRole.Staff);
        var query = Query(
            fixture.User.Id,
            fixture.Shop.Id,
            fixture.Sale.Id,
            fixture.SaleItem.Id,
            quantity: 1m,
            approvedRefundAmount: 0m);

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.False(result.Value.HasFinancialAccess);
        Assert.Null(result.Value.Financial);
        Assert.Null(result.Value.Lines[0].Financial);
        Assert.Equal(fixture.SaleItem.Id, result.Value.Lines[0].SaleItemId);
        Assert.Equal(fixture.SaleItem.InventoryBatchId, result.Value.Lines[0].InventoryBatchId);
        Assert.Empty(result.Value.Warnings);
    }

    [Fact]
    public async Task Handle_SaleReturnPreview_WhenActiveShopSaleMissing_ReturnsSaleNotFound()
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Shop", "Address", "City", "State", "560001", null, null, null);
        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, true));
        _saleRepository.GetByIdAsync(Arg.Any<Guid>(), shop.Id, Arg.Any<CancellationToken>()).Returns((Sale?)null);

        var result = await CreateHandler().Handle(
            Query(user.Id, shop.Id, Guid.NewGuid(), Guid.NewGuid(), quantity: 1m),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Sale.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_SaleReturnPreview_WhenSaleItemDoesNotBelongToSale_ReturnsValidationError()
    {
        var fixture = ArrangeSale(ShopRole.Owner);

        var result = await CreateHandler().Handle(
            Query(fixture.User.Id, fixture.Shop.Id, fixture.Sale.Id, Guid.NewGuid(), quantity: 1m),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("SaleReturn.SaleItemNotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_SaleReturnPreview_WhenQuantityIsNotPositive_ReturnsValidationError()
    {
        var fixture = ArrangeSale(ShopRole.Owner);

        var result = await CreateHandler().Handle(
            Query(fixture.User.Id, fixture.Shop.Id, fixture.Sale.Id, fixture.SaleItem.Id, quantity: 0m),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("SaleReturn.QuantityMustBePositive", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_SaleReturnPreview_WhenQuantityExceedsRemainingNonVoidedReturns_ReturnsValidationError()
    {
        var fixture = ArrangeSale(ShopRole.Owner);
        var previousReturn = MakeSaleReturn(fixture.Shop.Id, fixture.Sale.Id, fixture.SaleItem.Id, quantity: 4m);
        _saleReturnRepository.GetBySaleAsync(fixture.Shop.Id, fixture.Sale.Id, Arg.Any<CancellationToken>())
            .Returns([previousReturn]);

        var result = await CreateHandler().Handle(
            Query(fixture.User.Id, fixture.Shop.Id, fixture.Sale.Id, fixture.SaleItem.Id, quantity: 2m),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("SaleReturn.QuantityExceedsRemaining", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_SaleReturnPreview_IgnoresVoidedReturnsForRemainingQuantity()
    {
        var fixture = ArrangeSale(ShopRole.Owner);
        var voidedReturn = MakeSaleReturn(fixture.Shop.Id, fixture.Sale.Id, fixture.SaleItem.Id, quantity: 5m, voided: true);
        _saleReturnRepository.GetBySaleAsync(fixture.Shop.Id, fixture.Sale.Id, Arg.Any<CancellationToken>())
            .Returns([voidedReturn]);

        var result = await CreateHandler().Handle(
            Query(fixture.User.Id, fixture.Shop.Id, fixture.Sale.Id, fixture.SaleItem.Id, quantity: 5m),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(5m, result.Value.Lines[0].ReturnableQuantity);
    }

    [Fact]
    public async Task Handle_SaleReturnPreview_WhenRestockableOriginalBatchIsVoided_ReturnsValidationError()
    {
        var fixture = ArrangeSale(ShopRole.Owner);
        fixture.Batch.Void(fixture.User.Id);

        var result = await CreateHandler().Handle(
            Query(fixture.User.Id, fixture.Shop.Id, fixture.Sale.Id, fixture.SaleItem.Id, quantity: 1m),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("SaleReturn.BatchVoided", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_SaleReturnPreview_WhenRestockableOriginalBatchIsExpired_ReturnsValidationError()
    {
        var fixture = ArrangeSale(ShopRole.Owner, expiryDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

        var result = await CreateHandler().Handle(
            Query(fixture.User.Id, fixture.Shop.Id, fixture.Sale.Id, fixture.SaleItem.Id, quantity: 1m),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("SaleReturn.BatchExpired", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_SaleReturnPreview_WhenWastageOriginalBatchIsExpired_AllowsPreview()
    {
        var fixture = ArrangeSale(ShopRole.Owner, expiryDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

        var result = await CreateHandler().Handle(
            Query(
                fixture.User.Id,
                fixture.Shop.Id,
                fixture.Sale.Id,
                fixture.SaleItem.Id,
                quantity: 1m,
                condition: SaleReturnCondition.Wastage),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.False(result.Value.Lines[0].WillRestock);
    }

    [Fact]
    public async Task Handle_SaleReturnPreview_ReturnsOverrideWarningsWithoutWritingData()
    {
        var fixture = ArrangeSale(ShopRole.Owner);
        var query = Query(
            fixture.User.Id,
            fixture.Shop.Id,
            fixture.Sale.Id,
            fixture.SaleItem.Id,
            quantity: 1m,
            dueReductionOverrideAmount: 0m);

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains(result.Value.Warnings, w => w.Code == "sale_return.due_override");
        Assert.Contains(result.Value.Warnings, w => w.Code == "sale_return.note_required.due_override");
        _saleRepository.DidNotReceive().Update(Arg.Any<Sale>());
        await _saleReturnRepository.DidNotReceive().AddAsync(Arg.Any<SaleReturn>(), Arg.Any<CancellationToken>());
        _inventoryBatchRepository.DidNotReceive().Update(Arg.Any<InventoryBatch>());
    }

    private SalePreviewFixture ArrangeSale(ShopRole role, DateOnly? expiryDate = null)
    {
        var user = User.CreateWithEmail("user@test.com", "hash", "Test", "User");
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
            expiryDate,
            manufacturingDate: null,
            supplierId: null,
            user.Id).Value;
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
            Guid.NewGuid(),
            "Customer",
            null,
            PaymentMethod.Credit,
            DateTimeOffset.UtcNow,
            paidAmount: 300m,
            dueAmount: 250m,
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

        return new SalePreviewFixture(user, shop, sale, saleItem, batch);
    }

    private static PreviewSaleReturnQuery Query(
        Guid userId,
        Guid shopId,
        Guid saleId,
        Guid saleItemId,
        decimal quantity,
        SaleReturnCondition condition = SaleReturnCondition.Restockable,
        decimal? approvedRefundAmount = null,
        decimal? dueReductionOverrideAmount = null) =>
        new(
            userId,
            shopId,
            saleId,
            dueReductionOverrideAmount,
            DueOverrideReason: null,
            [new PreviewSaleReturnItemQuery(saleItemId, quantity, condition, approvedRefundAmount, Notes: null)]);

    private static SaleReturn MakeSaleReturn(
        Guid shopId,
        Guid saleId,
        Guid saleItemId,
        decimal quantity,
        bool voided = false)
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

        var saleReturn = SaleReturn.Create(
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

        if (voided)
            saleReturn.Void(DateTimeOffset.UtcNow, Guid.NewGuid(), "Mistake");

        return saleReturn;
    }

    private sealed record SalePreviewFixture(
        User User,
        Shop Shop,
        Sale Sale,
        SaleItem SaleItem,
        InventoryBatch Batch);
}
