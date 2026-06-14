using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Queries.GetSaleDetail;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Queries.GetSaleDetail;

public class GetSaleDetailQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly ISaleReturnRepository _saleReturnRepository = Substitute.For<ISaleReturnRepository>();
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();
    private readonly ICreditNoteRepository _creditNoteRepository = Substitute.For<ICreditNoteRepository>();

    private GetSaleDetailQueryHandler CreateHandler() =>
        new(_userRepository, _shopRepository, _saleRepository, _saleReturnRepository, _itemRepository, _creditNoteRepository);

    private static User MakeUser() =>
        User.CreateWithEmail("test@test.com", "hash", "Test", "User");

    private static Shop MakeShop() =>
        Shop.Create("Test Shop", "123 Main St", "City", "State", "560001", null, null, null);

    private static ShopMembership MakeMembership(Guid shopId, Guid userId) =>
        ShopMembership.Create(shopId, userId, ShopRole.Owner, true);

    private static SaleItem MakeSaleItem(Guid shopId, Guid itemId, decimal quantity = 5m, string lineName = "Rice", string lineCode = "BC-001") =>
        SaleItem.CreateGoods(
            shopId,
            itemId,
            Guid.NewGuid(),
            lineName: lineName,
            lineCode: lineCode,
            quantity,
            costPrice: 80m,
            salesPrice: 100m,
            mrp: 120m,
            taxRatePercent: 10m,
            isPriceIncludingTax: false,
            hasPriceMismatch: false);

    private static SaleItem MakeServiceSaleItem(Guid shopId, Guid serviceId, decimal quantity = 1m, string lineName = "Consultation", string lineCode = "SRV-001") =>
        SaleItem.CreateService(
            shopId,
            serviceId,
            lineName: lineName,
            lineCode: lineCode,
            quantity,
            costPrice: 0m,
            salesPrice: 300m,
            mrp: 0m,
            taxRatePercent: 0m,
            isPriceIncludingTax: false,
            hasPriceMismatch: false);

    private static Sale MakeSale(Guid shopId, SaleItem saleItem) =>
        Sale.Create(
            shopId,
            "INV-001",
            null,
            null,
            null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            paidAmount: 550m,
            dueAmount: 0m,
            totalAmount: 550m,
            totalTaxAmount: 50m,
            [saleItem]);

    private static SaleReturn MakeSaleReturn(
        Guid shopId,
        Guid saleId,
        Guid saleItemId,
        decimal quantity,
        bool voided = false,
        ReturnPayoutDestination payoutDestination = ReturnPayoutDestination.Refund)
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
            maxRefundAmount: quantity * 100m,
            approvedRefundAmount: quantity * 100m,
            taxableAmount: quantity * 100m,
            taxAmount: quantity * 10m,
            notes: "Accepted").Value;

        var line = new SaleReturnLineInput(
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
            shopId,
            saleId,
            $"RET-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            "Customer returned items",
            totalRefundAmount: quantity * 100m,
            dueReductionAmount: 0m,
            payoutAmount: quantity * 100m,
            payoutDestination: payoutDestination,
            totalTaxableAmount: quantity * 100m,
            totalTaxAmount: quantity * 10m,
            customerBalanceBefore: null,
            customerBalanceAfter: null,
            [line]).Value;

        if (voided)
            saleReturn.Void(DateTimeOffset.UtcNow, Guid.NewGuid(), "Mistake");

        return saleReturn;
    }

    private void ArrangeAuthorizedSale(User user, Shop shop, Sale sale, Item item)
    {
        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns(MakeMembership(shop.Id, user.Id));
        _saleRepository.GetByIdAsync(sale.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(sale);
        _itemRepository.GetByIdsAsync(shop.Id, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([item]);
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNotFoundError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();

        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSaleDetailQuery(userId, shopId, saleId), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("User.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenShopNotFound_ReturnsShopNotFoundError()
    {
        var user = MakeUser();
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>()).Returns((Shop?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSaleDetailQuery(user.Id, shopId, saleId), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.ShopNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenMembershipNotFound_ReturnsMembershipNotFoundError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var saleId = Guid.NewGuid();

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns((ShopMembership?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSaleDetailQuery(user.Id, shop.Id, saleId), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenSaleNotFound_ReturnsNotFoundError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var saleId = Guid.NewGuid();

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByIdAsync(saleId, shop.Id, Arg.Any<CancellationToken>()).Returns((Sale?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSaleDetailQuery(user.Id, shop.Id, saleId), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Sale.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenSaleFoundWithoutReturns_ReturnsReturnAwareLineDefaults()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var item = Item.Create(shop.Id, "Rice", "desc", "kg", "BC-001", true, Guid.NewGuid());
        var saleItem = MakeSaleItem(shop.Id, item.Id);
        var sale = MakeSale(shop.Id, saleItem);

        ArrangeAuthorizedSale(user, shop, sale, item);
        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>()).Returns([]);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSaleDetailQuery(user.Id, shop.Id, sale.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value.Items);
        Assert.Equal("Rice", result.Value.Items[0].ItemName);
        Assert.Empty(result.Value.Returns);
        Assert.Equal(0m, result.Value.Items[0].ReturnedQuantity);
        Assert.Equal(5m, result.Value.Items[0].ReturnableQuantity);
        Assert.Equal("NotReturned", result.Value.Items[0].ReturnStatus);
    }

    [Fact]
    public async Task Handle_WhenMultipleReturnsHaveCreditNotes_ReturnsCreditNoteSummariesInBulk()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var item = Item.Create(shop.Id, "Rice", "desc", "kg", "BC-001", true, Guid.NewGuid());
        var saleItem = MakeSaleItem(shop.Id, item.Id);
        var sale = MakeSale(shop.Id, saleItem);
        var saleReturn1 = MakeSaleReturn(shop.Id, sale.Id, saleItem.Id, quantity: 1m, payoutDestination: ReturnPayoutDestination.CreditNote);
        var saleReturn2 = MakeSaleReturn(shop.Id, sale.Id, saleItem.Id, quantity: 1m, payoutDestination: ReturnPayoutDestination.CreditNote);
        var creditNote1 = CreditNote.Issue(shop.Id, saleReturn1.Id, 100m, "Return credit", "CN-20260614-ABC123", null).Value;
        var creditNote2 = CreditNote.Issue(shop.Id, saleReturn2.Id, 50m, "Return credit", "CN-20260614-DEF456", null).Value;

        ArrangeAuthorizedSale(user, shop, sale, item);
        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>()).Returns([saleReturn1, saleReturn2]);
        _creditNoteRepository.GetByReturnIdsAsync(shop.Id, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([creditNote1, creditNote2]);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSaleDetailQuery(user.Id, shop.Id, sale.Id), CancellationToken.None);

        Assert.False(result.IsError);
        var returns = result.Value.Returns;
        Assert.Equal(2, returns.Count);
        Assert.Collection(returns,
            first =>
            {
                Assert.NotNull(first.CreditNote);
                Assert.Equal(creditNote1.Id, first.CreditNote!.CreditNoteId);
                Assert.Equal(creditNote1.Code, first.CreditNote.Code);
            },
            second =>
            {
                Assert.NotNull(second.CreditNote);
                Assert.Equal(creditNote2.Id, second.CreditNote!.CreditNoteId);
                Assert.Equal(creditNote2.Code, second.CreditNote.Code);
            });
        await _creditNoteRepository.Received(1).GetByReturnIdsAsync(
            shop.Id,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(saleReturn1.Id) && ids.Contains(saleReturn2.Id)),
            Arg.Any<CancellationToken>());
        await _creditNoteRepository.DidNotReceive().GetByReturnIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenSaleHasCreditNoteRedemptions_ReturnsRedemptionSummaries()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var item = Item.Create(shop.Id, "Rice", "desc", "kg", "BC-001", true, Guid.NewGuid());
        var saleItem = MakeSaleItem(shop.Id, item.Id);
        var sale = MakeSale(shop.Id, saleItem);
        IReadOnlyList<CreditNoteRedemptionListRow> redemptions =
            [
                new CreditNoteRedemptionListRow(Guid.NewGuid(), "CN-001", 60m),
                new CreditNoteRedemptionListRow(Guid.NewGuid(), "CN-002", 40m),
            ];

        ArrangeAuthorizedSale(user, shop, sale, item);
        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>()).Returns([]);
        _creditNoteRepository.GetRedemptionsBySaleIdAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>()).Returns(redemptions);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSaleDetailQuery(user.Id, shop.Id, sale.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.CreditNoteRedemptions.Count);
        Assert.Equal("CN-001", result.Value.CreditNoteRedemptions[0].Code);
        Assert.Equal(60m, result.Value.CreditNoteRedemptions[0].AppliedAmount);
        Assert.Equal("CN-002", result.Value.CreditNoteRedemptions[1].Code);
        Assert.Equal(40m, result.Value.CreditNoteRedemptions[1].AppliedAmount);
    }

    [Fact]
    public async Task Handle_WhenSaleContainsServiceLine_ReturnsServiceLineInDto()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var item = Item.Create(shop.Id, "Rice", "desc", "kg", "BC-001", true, Guid.NewGuid());
        var goods = MakeSaleItem(shop.Id, item.Id, quantity: 2m);
        var serviceId = Guid.NewGuid();
        var service = MakeServiceSaleItem(shop.Id, serviceId, quantity: 1m);
        var sale = Sale.Create(
            shop.Id,
            "INV-002",
            null,
            null,
            null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            paidAmount: 500m,
            dueAmount: 0m,
            totalAmount: 500m,
            totalTaxAmount: 20m,
            [goods, service]);

        ArrangeAuthorizedSale(user, shop, sale, item);
        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>()).Returns([]);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSaleDetailQuery(user.Id, shop.Id, sale.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Items.Count);
        var serviceLine = Assert.Single(result.Value.Items, i => i.LineType == SaleLineType.Service);
        Assert.Equal(serviceId, serviceLine.ServiceId);
        Assert.Null(serviceLine.ItemId);
        Assert.Null(serviceLine.InventoryBatchId);
        Assert.Equal("Consultation", serviceLine.ItemName);
        Assert.Equal("SRV-001", serviceLine.LineCode);
        Assert.Equal(1m, serviceLine.ReturnableQuantity);
    }

    [Fact]
    public async Task Handle_WhenServiceLineHasActiveReturn_ComputesReturnedAndStatus()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var item = Item.Create(shop.Id, "Rice", "desc", "kg", "BC-001", true, Guid.NewGuid());
        var goods = MakeSaleItem(shop.Id, item.Id, quantity: 2m);
        var service = MakeServiceSaleItem(shop.Id, Guid.NewGuid(), quantity: 2m);
        var sale = Sale.Create(
            shop.Id,
            "INV-003",
            null,
            null,
            null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            paidAmount: 800m,
            dueAmount: 0m,
            totalAmount: 800m,
            totalTaxAmount: 20m,
            [goods, service]);
        var serviceReturn = MakeSaleReturn(shop.Id, sale.Id, service.Id, quantity: 1m);

        ArrangeAuthorizedSale(user, shop, sale, item);
        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>()).Returns([serviceReturn]);

        var result = await CreateHandler().Handle(new GetSaleDetailQuery(user.Id, shop.Id, sale.Id), CancellationToken.None);

        Assert.False(result.IsError);
        var serviceLine = Assert.Single(result.Value.Items, i => i.LineType == SaleLineType.Service);
        Assert.Equal(1m, serviceLine.ReturnedQuantity);
        Assert.Equal(1m, serviceLine.ReturnableQuantity);
        Assert.Equal("PartiallyReturned", serviceLine.ReturnStatus);
    }

    [Fact]
    public async Task Handle_WhenSaleHasNoDiscounts_ExposesZeroDiscountBreakdown()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var item = Item.Create(shop.Id, "Rice", "desc", "kg", "BC-001", true, Guid.NewGuid());
        var saleItem = MakeSaleItem(shop.Id, item.Id);
        var sale = MakeSale(shop.Id, saleItem);

        ArrangeAuthorizedSale(user, shop, sale, item);
        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>()).Returns([]);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSaleDetailQuery(user.Id, shop.Id, sale.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(550m, result.Value.TotalBeforeDiscount);
        Assert.Equal(0m, result.Value.TotalDiscountAmount);
        Assert.Equal(50m, result.Value.TotalTaxAmount);
        Assert.Equal(550m, result.Value.TotalAmount);

        var line = Assert.Single(result.Value.Items);
        Assert.Equal(100m, line.OriginalSalesPrice);
        Assert.Equal(100m, line.FinalSalesPrice);
        Assert.Equal(500m, line.PreTaxAmountBeforeDiscount);
        Assert.Equal(0m, line.ItemDiscountAmount);
        Assert.Equal(0m, line.SaleDiscountAmount);
        Assert.Equal(500m, line.TaxableAmount);
        Assert.Equal(50m, line.TaxAmount);
        Assert.Equal(550m, line.TotalAmount);
        Assert.Equal(0m, line.SavingsAmount);
    }

    [Fact]
    public async Task Handle_WhenSaleHasDiscounts_ExposesLineDiscountBreakdown()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var item = Item.Create(shop.Id, "Rice", "desc", "kg", "BC-001", true, Guid.NewGuid());
        var saleItem = SaleItem.CreateGoods(
            shop.Id,
            item.Id,
            Guid.NewGuid(),
            lineName: item.Name,
            lineCode: item.Barcode,
            quantity: 5m,
            costPrice: 80m,
            salesPrice: 100m,
            mrp: 120m,
            taxRatePercent: 10m,
            isPriceIncludingTax: false,
            hasPriceMismatch: false,
            itemDiscountAmount: 20m,
            saleDiscountAmount: 30m);
        var sale = Sale.Create(
            shop.Id,
            "INV-001",
            null,
            null,
            null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            paidAmount: 495m,
            dueAmount: 0m,
            totalAmount: 495m,
            totalTaxAmount: 45m,
            [saleItem],
            subtotalBeforeDiscount: 500m,
            totalBeforeDiscount: 550m,
            totalDiscountAmount: 50m);

        ArrangeAuthorizedSale(user, shop, sale, item);
        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>()).Returns([]);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSaleDetailQuery(user.Id, shop.Id, sale.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(550m, result.Value.TotalBeforeDiscount);
        Assert.Equal(50m, result.Value.TotalDiscountAmount);
        Assert.Equal(45m, result.Value.TotalTaxAmount);
        Assert.Equal(495m, result.Value.TotalAmount);

        var line = Assert.Single(result.Value.Items);
        Assert.Equal(100m, line.OriginalSalesPrice);
        Assert.Equal(100m, line.FinalSalesPrice);
        Assert.Equal(500m, line.PreTaxAmountBeforeDiscount);
        Assert.Equal(20m, line.ItemDiscountAmount);
        Assert.Equal(30m, line.SaleDiscountAmount);
        Assert.Equal(450m, line.TaxableAmount);
        Assert.Equal(45m, line.TaxAmount);
        Assert.Equal(495m, line.TotalAmount);
        Assert.Equal(50m, line.SavingsAmount);
    }

    [Fact]
    public async Task Handle_WhenSaleHasPartialReturn_ExposesReturnHistoryAndLineQuantities()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var item = Item.Create(shop.Id, "Rice", "desc", "kg", "BC-001", true, Guid.NewGuid());
        var saleItem = MakeSaleItem(shop.Id, item.Id, quantity: 5m);
        var sale = MakeSale(shop.Id, saleItem);
        var saleReturn = MakeSaleReturn(shop.Id, sale.Id, saleItem.Id, quantity: 2m);

        ArrangeAuthorizedSale(user, shop, sale, item);
        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>()).Returns([saleReturn]);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSaleDetailQuery(user.Id, shop.Id, sale.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value.Returns);
        Assert.Equal(saleReturn.Id, result.Value.Returns[0].SaleReturnId);
        Assert.Equal(2m, result.Value.Returns[0].Items[0].Quantity);
        Assert.Equal(2m, result.Value.Items[0].ReturnedQuantity);
        Assert.Equal(3m, result.Value.Items[0].ReturnableQuantity);
        Assert.Equal("PartiallyReturned", result.Value.Items[0].ReturnStatus);
        Assert.Equal(550m, result.Value.TotalAmount);
        Assert.Equal(50m, result.Value.TotalTaxAmount);
        Assert.Equal(550m, result.Value.PaidAmount);
    }

    [Fact]
    public async Task Handle_WhenSaleHasCreditNoteReturn_ExposesPayoutDestination()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var item = Item.Create(shop.Id, "Rice", "desc", "kg", "BC-001", true, Guid.NewGuid());
        var saleItem = MakeSaleItem(shop.Id, item.Id, quantity: 1m);
        var sale = MakeSale(shop.Id, saleItem);
        var saleReturn = MakeSaleReturn(
            shop.Id,
            sale.Id,
            saleItem.Id,
            quantity: 1m,
            payoutDestination: ReturnPayoutDestination.CreditNote);

        ArrangeAuthorizedSale(user, shop, sale, item);
        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>()).Returns([saleReturn]);

        var result = await CreateHandler().Handle(new GetSaleDetailQuery(user.Id, shop.Id, sale.Id), CancellationToken.None);

        Assert.False(result.IsError);
        var returnedSale = Assert.Single(result.Value.Returns);
        Assert.Equal(ReturnPayoutDestination.CreditNote, returnedSale.PayoutDestination);
    }

    [Fact]
    public async Task Handle_WhenSaleLineFullyReturned_ReturnableQuantityIsZero()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var item = Item.Create(shop.Id, "Rice", "desc", "kg", "BC-001", true, Guid.NewGuid());
        var saleItem = MakeSaleItem(shop.Id, item.Id, quantity: 5m);
        var sale = MakeSale(shop.Id, saleItem);
        var firstReturn = MakeSaleReturn(shop.Id, sale.Id, saleItem.Id, quantity: 2m);
        var secondReturn = MakeSaleReturn(shop.Id, sale.Id, saleItem.Id, quantity: 3m);

        ArrangeAuthorizedSale(user, shop, sale, item);
        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>())
            .Returns([firstReturn, secondReturn]);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSaleDetailQuery(user.Id, shop.Id, sale.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Returns.Count);
        Assert.Equal(5m, result.Value.Items[0].ReturnedQuantity);
        Assert.Equal(0m, result.Value.Items[0].ReturnableQuantity);
        Assert.Equal("FullyReturned", result.Value.Items[0].ReturnStatus);
        Assert.Equal(550m, result.Value.TotalAmount);
        Assert.Equal(50m, result.Value.TotalTaxAmount);
    }

    [Fact]
    public async Task Handle_WhenReturnIsVoided_IgnoresItForHistoryAndQuantities()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var item = Item.Create(shop.Id, "Rice", "desc", "kg", "BC-001", true, Guid.NewGuid());
        var saleItem = MakeSaleItem(shop.Id, item.Id, quantity: 5m);
        var sale = MakeSale(shop.Id, saleItem);
        var voidedReturn = MakeSaleReturn(shop.Id, sale.Id, saleItem.Id, quantity: 5m, voided: true);

        ArrangeAuthorizedSale(user, shop, sale, item);
        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>()).Returns([voidedReturn]);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSaleDetailQuery(user.Id, shop.Id, sale.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(result.Value.Returns);
        Assert.Equal(0m, result.Value.Items[0].ReturnedQuantity);
        Assert.Equal(5m, result.Value.Items[0].ReturnableQuantity);
        Assert.Equal("NotReturned", result.Value.Items[0].ReturnStatus);
    }
}
