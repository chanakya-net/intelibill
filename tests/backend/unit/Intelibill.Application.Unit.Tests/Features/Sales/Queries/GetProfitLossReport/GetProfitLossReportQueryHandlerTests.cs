using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Queries.GetProfitLossReport;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Queries.GetProfitLossReport;

public class GetProfitLossReportQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly ISaleReturnRepository _saleReturnRepository = Substitute.For<ISaleReturnRepository>();
    private readonly IInventoryAdjustmentRepository _inventoryAdjustmentRepository = Substitute.For<IInventoryAdjustmentRepository>();

    private GetProfitLossReportQueryHandler CreateHandler() =>
        new(_userRepository, _shopRepository, _saleRepository, _saleReturnRepository, _inventoryAdjustmentRepository);

    private static User MakeUser() =>
        User.CreateWithEmail("sales@test.com", "hash", "Sales", "User");

    private static Shop MakeShop() =>
        Shop.Create("Test Shop", "123 Main St", "City", "State", "560001", null, null, null);

    private static ShopMembership MakeMembership(Guid shopId, Guid userId) =>
        ShopMembership.Create(shopId, userId, ShopRole.Owner, true);

    [Fact]
    public async Task Handle_CalculatesProfitLossCorrectly()
    {
        // Arrange
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);

        // Item 1: Price includes tax. 110 total, 10% tax -> 100 base, 10 tax. Cost 80. Profit Before Tax: 20. Profit After Tax: 10.
        var item1 = SaleItem.CreateGoods(shop.Id, Guid.NewGuid(), Guid.NewGuid(), lineName: "Item", lineCode: "BC-001", 1, 80, 110, 120, 10, true, false);
        // Item 2: Price excludes tax. 200 base, 5% tax -> 200 base, 10 tax. Cost 150. Profit Before Tax: 50. Profit After Tax: 40.
        var item2 = SaleItem.CreateGoods(shop.Id, Guid.NewGuid(), Guid.NewGuid(), lineName: "Item", lineCode: "BC-001", 1, 150, 200, 250, 5, false, false);

        var sale = Sale.Create(
            shop.Id, "INV-001", null, "John Doe", null, PaymentMethod.Cash,
            DateTimeOffset.Now, 320, 0, 320, 20, new List<SaleItem> { item1, item2 });

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(new[] { sale });

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new GetProfitLossReportQuery(user.Id, shop.Id), CancellationToken.None);

        // Assert
        Assert.False(result.IsError);
        var report = result.Value[0];
        Assert.Equal(sale.Id, report.SaleId);
        Assert.Equal("INV-001", report.ReferenceNumber);
        Assert.Equal(sale.SoldAt, report.OccurredAt);
        Assert.Equal("John Doe", report.PartyName);
        Assert.Equal(ProfitLossReportRowTypes.Sale, report.RowType);
        Assert.Null(report.InventoryAdjustmentId);
        
        // Total Cost: 80 + 150 = 230
        Assert.Equal(230, report.TotalCost);
        
        // Revenue Before Tax: 100 + 200 = 300
        Assert.Equal(300, report.RevenueBeforeTax);
        
        // Revenue After Tax: 110 + 210 = 320
        Assert.Equal(320, report.RevenueAfterTax);
        
        // Profit Before Tax: 320 - 230 = 90
        Assert.Equal(90, report.ProfitBeforeTax);
        
        // Profit After Tax: 300 - 230 = 70
        Assert.Equal(70, report.ProfitAfterTax);
    }

    [Fact]
    public async Task Handle_CalculatesDiscountedProfitLossUsingFinalLineValues()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);

        var discountedItem = SaleItem.CreateGoods(
            shop.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            lineName: "Item",
            lineCode: "BC-001",
            1m,
            50m,
            100m,
            120m,
            10m,
            false,
            false,
            finalSalesPrice: 80m,
            itemDiscountAmount: 20m,
            taxableAmount: 80m,
            taxAmount: 8m,
            totalAmount: 88m);

        var sale = Sale.Create(
            shop.Id,
            "INV-DISC",
            null,
            "Discount Customer",
            null,
            PaymentMethod.Cash,
            DateTimeOffset.Now,
            88m,
            0m,
            88m,
            8m,
            [discountedItem]);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAsync(shop.Id, Arg.Any<CancellationToken>()).Returns([sale]);

        var result = await CreateHandler().Handle(new GetProfitLossReportQuery(user.Id, shop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        var report = result.Value[0];
        Assert.Equal(50, report.TotalCost);
        Assert.Equal(80, report.RevenueBeforeTax);
        Assert.Equal(88, report.RevenueAfterTax);
        Assert.Equal(38, report.ProfitBeforeTax);
        Assert.Equal(30, report.ProfitAfterTax);
    }

    [Fact]
    public async Task Handle_CalculatesLossCorrectly()
    {
        // Arrange
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);

        // Item: Cost 100, Sales Price 80 (Net), 10% tax -> 88 Gross.
        // Loss Before Tax: 88 - 100 = -12.
        // Loss After Tax: 80 - 100 = -20.
        var item = SaleItem.CreateGoods(shop.Id, Guid.NewGuid(), Guid.NewGuid(), lineName: "Item", lineCode: "BC-001", 1, 100, 80, 120, 10, false, false);

        var sale = Sale.Create(
            shop.Id, "INV-002", null, "Jane Doe", null, PaymentMethod.Cash,
            DateTimeOffset.Now, 88, 0, 88, 8, new List<SaleItem> { item });

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(new[] { sale });

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new GetProfitLossReportQuery(user.Id, shop.Id), CancellationToken.None);

        // Assert
        Assert.False(result.IsError);
        var report = result.Value[0];
        Assert.Equal(ProfitLossReportRowTypes.Sale, report.RowType);
        Assert.Null(report.InventoryAdjustmentId);
        
        Assert.Equal(100, report.TotalCost);
        Assert.Equal(80, report.RevenueBeforeTax);
        Assert.Equal(88, report.RevenueAfterTax);
        Assert.Equal(-12, report.ProfitBeforeTax);
        Assert.Equal(-20, report.ProfitAfterTax);
    }

    [Fact]
    public async Task Handle_AddsReturnAdjustmentRowsOnReturnDateAndExcludesVoidedReturns()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var saleItem = SaleItem.CreateGoods(shop.Id, Guid.NewGuid(), Guid.NewGuid(), lineName: "Item", lineCode: "BC-001", 2m, 60m, 100m, 120m, 10m, false, false);
        var sale = Sale.Create(
            shop.Id,
            "INV-003",
            null,
            "John Doe",
            null,
            PaymentMethod.Cash,
            new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero),
            220m,
            0m,
            220m,
            20m,
            [saleItem]);
        var restockableReturn = MakeReturn(
            shop.Id,
            sale.Id,
            saleItem.Id,
            "RET-RESTOCK",
            SaleReturnCondition.Restockable,
            quantity: 1m,
            approvedRefund: 110m,
            tax: 10m,
            processedAt: new DateTimeOffset(2026, 5, 3, 10, 0, 0, TimeSpan.Zero));
        var partialRefundReturn = MakeReturn(
            shop.Id,
            sale.Id,
            saleItem.Id,
            "RET-PARTIAL",
            SaleReturnCondition.Restockable,
            quantity: 1m,
            approvedRefund: 55m,
            tax: 10m,
            processedAt: new DateTimeOffset(2026, 5, 2, 10, 0, 0, TimeSpan.Zero));
        var wastageReturn = MakeReturn(
            shop.Id,
            sale.Id,
            saleItem.Id,
            "RET-WASTE",
            SaleReturnCondition.Wastage,
            quantity: 1m,
            approvedRefund: 0m,
            tax: 10m,
            processedAt: new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));
        var voidedReturn = MakeReturn(
            shop.Id,
            sale.Id,
            saleItem.Id,
            "RET-VOID",
            SaleReturnCondition.Restockable,
            quantity: 1m,
            approvedRefund: 110m,
            tax: 10m,
            processedAt: new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero));
        voidedReturn.Void(DateTimeOffset.UtcNow, user.Id, "Mistake");

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAsync(shop.Id, Arg.Any<CancellationToken>()).Returns([sale]);
        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>())
            .Returns([restockableReturn, partialRefundReturn, wastageReturn, voidedReturn]);

        var result = await CreateHandler().Handle(new GetProfitLossReportQuery(user.Id, shop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(4, result.Value.Count);
        var wastageRow = result.Value.Single(r => r.ReferenceNumber.Contains("RET-WASTE"));
        Assert.Equal(sale.Id, wastageRow.SaleId);
        Assert.Equal(wastageReturn.ProcessedAt, wastageRow.OccurredAt);
        Assert.Equal("John Doe", wastageRow.PartyName);
        Assert.Equal(ProfitLossReportRowTypes.SaleReturn, wastageRow.RowType);
        Assert.Null(wastageRow.InventoryAdjustmentId);
        Assert.Equal(0m, wastageRow.TotalCost);
        Assert.Equal(60m, wastageRow.WastageCost);
        Assert.Equal(0m, wastageRow.RevenueBeforeTax);
        Assert.Equal(0m, wastageRow.RevenueAfterTax);
        var restockRow = result.Value.Single(r => r.ReferenceNumber.Contains("RET-RESTOCK"));
        Assert.Equal(ProfitLossReportRowTypes.SaleReturn, restockRow.RowType);
        Assert.Null(restockRow.InventoryAdjustmentId);
        Assert.Equal(-60m, restockRow.TotalCost);
        Assert.Equal(0m, restockRow.WastageCost);
        Assert.Equal(-100m, restockRow.RevenueBeforeTax);
        Assert.Equal(-110m, restockRow.RevenueAfterTax);
        var partialRow = result.Value.Single(r => r.ReferenceNumber.Contains("RET-PARTIAL"));
        Assert.Equal(-60m, partialRow.TotalCost);
        Assert.Equal(-50m, partialRow.RevenueBeforeTax);
        Assert.Equal(-55m, partialRow.RevenueAfterTax);
        Assert.DoesNotContain(result.Value, r => r.ReferenceNumber.Contains("RET-VOID"));
        var saleRow = result.Value.Single(r => r.ReferenceNumber == "INV-003");
        Assert.Equal(ProfitLossReportRowTypes.Sale, saleRow.RowType);
        Assert.Null(saleRow.InventoryAdjustmentId);
        Assert.Equal(120m, saleRow.TotalCost);
        Assert.Equal(220m, saleRow.RevenueAfterTax);
    }

    [Fact]
    public async Task Handle_AddsActiveDecreaseInventoryAdjustmentsAsLossRowsAndExcludesIncreasesAndVoidedAdjustments()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var saleItem = SaleItem.CreateGoods(shop.Id, Guid.NewGuid(), Guid.NewGuid(), lineName: "Item", lineCode: "BC-001", 1m, 60m, 100m, 120m, 10m, false, false);
        var sale = Sale.Create(
            shop.Id,
            "INV-ADJ",
            null,
            "Adjustment Customer",
            null,
            PaymentMethod.Cash,
            new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero),
            110m,
            0m,
            110m,
            10m,
            [saleItem]);
        var decrease = MakeAdjustment(
            shop.Id,
            "ADJ-LOSS",
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Damaged,
            costImpact: 45m,
            performedAt: new DateTimeOffset(2026, 5, 3, 10, 0, 0, TimeSpan.Zero),
            user.Id);
        var increase = MakeAdjustment(
            shop.Id,
            "ADJ-GAIN",
            InventoryAdjustmentDirection.Increase,
            InventoryAdjustmentReason.FoundStock,
            costImpact: 25m,
            performedAt: new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero),
            user.Id);
        var voidedDecrease = MakeAdjustment(
            shop.Id,
            "ADJ-VOID",
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Expired,
            costImpact: 35m,
            performedAt: new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero),
            user.Id);
        voidedDecrease.Void(DateTimeOffset.UtcNow, user.Id, "Incorrect adjustment", Guid.NewGuid());

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAsync(shop.Id, Arg.Any<CancellationToken>()).Returns([sale]);
        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>()).Returns([]);
        _inventoryAdjustmentRepository.GetProfitLossAdjustmentsAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns([decrease, increase, voidedDecrease]);

        var result = await CreateHandler().Handle(new GetProfitLossReportQuery(user.Id, shop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(["ADJ-LOSS", "INV-ADJ"], result.Value.Select(r => r.ReferenceNumber).ToArray());

        var adjustmentRow = result.Value[0];
        Assert.Null(adjustmentRow.SaleId);
        Assert.Equal(decrease.Id, adjustmentRow.InventoryAdjustmentId);
        Assert.Equal("ADJ-LOSS", adjustmentRow.ReferenceNumber);
        Assert.Equal(decrease.PerformedAt, adjustmentRow.OccurredAt);
        Assert.Null(adjustmentRow.PartyName);
        Assert.Equal(ProfitLossReportRowTypes.InventoryAdjustment, adjustmentRow.RowType);
        Assert.Equal(0m, adjustmentRow.TotalCost);
        Assert.Equal(45m, adjustmentRow.WastageCost);
        Assert.Equal(0m, adjustmentRow.RevenueBeforeTax);
        Assert.Equal(0m, adjustmentRow.RevenueAfterTax);
        Assert.Equal(-45m, adjustmentRow.ProfitBeforeTax);
        Assert.Equal(-45m, adjustmentRow.ProfitAfterTax);
        Assert.DoesNotContain(result.Value, r => r.ReferenceNumber is "ADJ-GAIN" or "ADJ-VOID");
    }

    private static SaleReturn MakeReturn(
        Guid shopId,
        Guid saleId,
        Guid saleItemId,
        string returnNumber,
        SaleReturnCondition condition,
        decimal quantity,
        decimal approvedRefund,
        decimal tax,
        DateTimeOffset processedAt)
    {
        var item = SaleReturnItem.Create(
            shopId,
            saleId,
            saleItemId,
            quantity,
            condition,
            originalCostPrice: 60m,
            originalSalesPrice: 100m,
            originalTaxRatePercent: 10m,
            originalIsPriceIncludingTax: false,
            maxRefundAmount: 110m,
            approvedRefund,
            taxableAmount: 100m,
            taxAmount: tax,
            notes: "Return").Value;

        var line = new SaleReturnLineInput(
            item.ShopId,
            item.SaleItemId,
            item.Quantity,
            item.Condition,
            item.OriginalCostPrice,
            item.OriginalSalesPrice,
            item.OriginalTaxRatePercent,
            item.OriginalIsPriceIncludingTax,
            item.MaxRefundAmount,
            item.ApprovedRefundAmount,
            item.TaxableAmount,
            item.TaxAmount,
            item.Notes);

        return SaleReturn.Record(
            shopId,
            saleId,
            returnNumber,
            processedAt,
            Guid.NewGuid(),
            notes: null,
            totalRefundAmount: approvedRefund,
            dueReductionAmount: 0m,
            payoutAmount: approvedRefund,
            payoutMethod: PaymentMethod.Cash,
            totalTaxableAmount: 100m,
            totalTaxAmount: tax,
            customerBalanceBefore: null,
            customerBalanceAfter: null,
            [line]).Value;
    }

    [Fact]
    public async Task Handle_ServiceSale_CalculatesProfitLossWithZeroCost()
    {
        // Arrange
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);

        // Service item: price 150, 10% tax -> 136.36 base, 13.64 tax. Cost 50 (which must be ignored/remain 0 in v1).
        var serviceItem = SaleItem.CreateService(
            shop.Id, Guid.NewGuid(), lineName: "Consulting", lineCode: "SRV-001",
            quantity: 1m, costPrice: 50m, salesPrice: 150m, mrp: 150m, taxRatePercent: 10m,
            isPriceIncludingTax: true, hasPriceMismatch: false);

        var sale = Sale.Create(
            shop.Id, "INV-SRV", null, "John Doe", null, PaymentMethod.Cash,
            DateTimeOffset.Now, 150m, 0m, 150m, 13.64m, new List<SaleItem> { serviceItem });

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(new[] { sale });

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new GetProfitLossReportQuery(user.Id, shop.Id), CancellationToken.None);

        // Assert
        Assert.False(result.IsError);
        var report = result.Value[0];
        Assert.Equal(sale.Id, report.SaleId);
        Assert.Equal("INV-SRV", report.ReferenceNumber);
        
        // Total Cost must be 0 for service
        Assert.Equal(0m, report.TotalCost);
        
        // Revenue Before Tax: 136.36
        Assert.Equal(serviceItem.TaxableAmount, report.RevenueBeforeTax);
        
        // Revenue After Tax: 150.00
        Assert.Equal(150m, report.RevenueAfterTax);
        
        // Profit Before Tax: 150 - 0 = 150
        Assert.Equal(150m, report.ProfitBeforeTax);
        
        // Profit After Tax: 136.36 - 0 = 136.36
        Assert.Equal(serviceItem.TaxableAmount, report.ProfitAfterTax);
    }

    private static InventoryAdjustment MakeAdjustment(
        Guid shopId,
        string adjustmentNumber,
        InventoryAdjustmentDirection direction,
        InventoryAdjustmentReason reason,
        decimal costImpact,
        DateTimeOffset performedAt,
        Guid userId)
    {
        const decimal quantity = 1m;
        var quantityBefore = direction == InventoryAdjustmentDirection.Decrease ? 5m : 4m;
        var quantityAfter = direction == InventoryAdjustmentDirection.Decrease ? 4m : 5m;

        return InventoryAdjustment.Create(
            shopId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            adjustmentNumber,
            direction,
            reason,
            quantity,
            unitCost: costImpact,
            costImpact,
            batchQuantityBefore: quantityBefore,
            batchQuantityAfter: quantityAfter,
            inventoryQuantityBefore: quantityBefore,
            inventoryQuantityAfter: quantityAfter,
            performedAt,
            performedBy: userId,
            notes: null,
            createdBy: userId).Value;
    }
}
