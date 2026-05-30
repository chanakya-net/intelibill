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
        new(
            _userRepository,
            _shopRepository,
            new ProfitLossReportBuilder(_saleRepository, _saleReturnRepository, _inventoryAdjustmentRepository));

    private static User MakeUser() =>
        User.CreateWithEmail("sales@test.com", "hash", "Sales", "User");

    private static Shop MakeShop() =>
        Shop.Create("Test Shop", "123 Main St", "City", "State", "560001", null, null, null);

    private static ShopMembership MakeMembership(Guid shopId, Guid userId) =>
        ShopMembership.Create(shopId, userId, ShopRole.Owner, true);

    [Fact]
    public async Task Handle_UsesDefaultWindowAndReturnsRowMargin()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var item = SaleItem.CreateGoods(shop.Id, Guid.NewGuid(), Guid.NewGuid(), "Item", "BC-001", 1m, 80m, 100m, 100m, 0m, false, false);
        var sale = Sale.Create(shop.Id, "INV-001", null, "John Doe", null, PaymentMethod.Cash, DateTimeOffset.UtcNow, 100m, 0m, 100m, 0m, [item]);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, today.AddDays(-6), today, Arg.Any<CancellationToken>()).Returns([sale]);
        _saleReturnRepository.GetByShopAndDateRangeAsync(shop.Id, today.AddDays(-6), today, Arg.Any<CancellationToken>()).Returns(Array.Empty<SaleReturn>());
        _inventoryAdjustmentRepository.GetByShopAndDateRangeAsync(shop.Id, today.AddDays(-6), today, Arg.Any<CancellationToken>()).Returns(Array.Empty<InventoryAdjustment>());

        var result = await CreateHandler().Handle(new GetProfitLossReportQuery(user.Id, shop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(today.AddDays(-6), result.Value.AppliedFilters.From);
        Assert.Equal(today, result.Value.AppliedFilters.To);
        Assert.Equal("all", result.Value.AppliedFilters.Type);
        Assert.Equal(1, result.Value.AppliedFilters.PageNumber);
        Assert.Equal(20, result.Value.AppliedFilters.PageSize);
        Assert.Equal(1, result.Value.TotalCount);

        var row = Assert.Single(result.Value.Items);
        Assert.Equal(80m, row.TotalCost);
        Assert.Equal(100m, row.RevenueAfterTax);
        Assert.Equal(20m, row.ProfitAfterTax);
        Assert.Equal(25m, row.MarginPercent);
        Assert.Equal(25m, result.Value.Summary.AverageMarginPercent);
        Assert.Equal(20m, result.Value.Summary.NetProfitAfterTax);
        Assert.Equal(100m, result.Value.Summary.RevenueIncludingTax);
        Assert.Equal(80m, result.Value.Summary.TotalCost);
        Assert.Equal(1, result.Value.Summary.InvoiceCount);
        Assert.Equal(0, result.Value.Summary.ReturnCount);
        Assert.Equal(0, result.Value.Summary.AdjustmentCount);
    }

    [Fact]
    public async Task Handle_PaginatesAndSummarizesFilteredRows()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var start = new DateOnly(2026, 5, 1);
        var end = new DateOnly(2026, 5, 7);

        var sale1 = Sale.Create(shop.Id, "INV-001", null, "Alpha", null, PaymentMethod.Cash, new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), 100m, 0m, 100m, 0m,
            [SaleItem.CreateGoods(shop.Id, Guid.NewGuid(), Guid.NewGuid(), "Item", "BC-001", 1m, 80m, 100m, 100m, 0m, false, false)]);
        var sale2 = Sale.Create(shop.Id, "INV-002", null, "Beta", null, PaymentMethod.Cash, new DateTimeOffset(2026, 5, 2, 10, 0, 0, TimeSpan.Zero), 120m, 0m, 120m, 0m,
            [SaleItem.CreateGoods(shop.Id, Guid.NewGuid(), Guid.NewGuid(), "Item", "BC-002", 1m, 90m, 120m, 120m, 0m, false, false)]);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, start, end, Arg.Any<CancellationToken>()).Returns([sale1, sale2]);
        _saleReturnRepository.GetByShopAndDateRangeAsync(shop.Id, start, end, Arg.Any<CancellationToken>()).Returns(Array.Empty<SaleReturn>());
        _inventoryAdjustmentRepository.GetByShopAndDateRangeAsync(shop.Id, start, end, Arg.Any<CancellationToken>()).Returns(Array.Empty<InventoryAdjustment>());

        var firstPage = await CreateHandler().Handle(new GetProfitLossReportQuery(user.Id, shop.Id, start, end, Page: 1, PageSize: 1), CancellationToken.None);
        var secondPage = await CreateHandler().Handle(new GetProfitLossReportQuery(user.Id, shop.Id, start, end, Page: 2, PageSize: 1), CancellationToken.None);

        Assert.False(firstPage.IsError);
        Assert.False(secondPage.IsError);
        Assert.Equal(2, firstPage.Value.TotalCount);
        Assert.Equal(2, firstPage.Value.Summary.InvoiceCount);
        Assert.Single(firstPage.Value.Items);
        Assert.Equal("INV-002", firstPage.Value.Items[0].ReferenceNumber);
        Assert.Equal("INV-001", secondPage.Value.Items[0].ReferenceNumber);
        Assert.Equal(50m, firstPage.Value.Summary.NetProfitAfterTax);
        Assert.Equal(220m, firstPage.Value.Summary.RevenueIncludingTax);
        Assert.Equal(170m, firstPage.Value.Summary.TotalCost);
        Assert.Equal(29.41m, firstPage.Value.Summary.AverageMarginPercent);
    }

    [Fact]
    public async Task Handle_SearchesReferenceNumbersCustomerNamesAndExactAmounts()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var from = new DateOnly(2026, 5, 1);
        var to = new DateOnly(2026, 5, 7);

        var saleItem = SaleItem.CreateGoods(shop.Id, Guid.NewGuid(), Guid.NewGuid(), "Item", "BC-001", 1m, 80m, 100m, 100m, 0m, false, false);
        var sale = Sale.Create(shop.Id, "INV-001", null, "Customer One", null, PaymentMethod.Cash, new DateTimeOffset(2026, 5, 2, 10, 0, 0, TimeSpan.Zero), 100m, 0m, 100m, 0m, [saleItem]);
        var saleReturn = SaleReturn.Record(
            shop.Id,
            sale.Id,
            "RET-001",
            new DateTimeOffset(2026, 5, 3, 10, 0, 0, TimeSpan.Zero),
            Guid.NewGuid(),
            null,
            100m,
            0m,
            100m,
            PaymentMethod.Cash,
            100m,
            0m,
            null,
            null,
            [
                new SaleReturnLineInput(shop.Id, saleItem.Id, 1m, SaleReturnCondition.Restockable, 80m, 100m, 0m, false, 100m, 100m, 100m, 0m, null)
            ]).Value;
        var adjustment = InventoryAdjustment.Create(
            shop.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ADJ-001",
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Damaged,
            1m,
            50m,
            50m,
            5m,
            4m,
            5m,
            4m,
            new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero),
            user.Id,
            null,
            user.Id).Value;

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, from, to, Arg.Any<CancellationToken>()).Returns([sale]);
        _saleReturnRepository.GetByShopAndDateRangeAsync(shop.Id, from, to, Arg.Any<CancellationToken>()).Returns([saleReturn]);
        _inventoryAdjustmentRepository.GetByShopAndDateRangeAsync(shop.Id, from, to, Arg.Any<CancellationToken>()).Returns([adjustment]);

        var customerSearch = await CreateHandler().Handle(new GetProfitLossReportQuery(user.Id, shop.Id, from, to, Search: "Customer"), CancellationToken.None);
        var returnSearch = await CreateHandler().Handle(new GetProfitLossReportQuery(user.Id, shop.Id, from, to, Search: "RET-001"), CancellationToken.None);
        var numericSearch = await CreateHandler().Handle(new GetProfitLossReportQuery(user.Id, shop.Id, from, to, Search: "80"), CancellationToken.None);
        var adjustmentSearch = await CreateHandler().Handle(new GetProfitLossReportQuery(user.Id, shop.Id, from, to, Search: "ADJ-001"), CancellationToken.None);

        Assert.Equal(2, customerSearch.Value.TotalCount);
        Assert.All(customerSearch.Value.Items, row => Assert.Contains("Customer", row.PartyName ?? row.ReferenceNumber));
        Assert.Single(returnSearch.Value.Items, row => row.ReferenceNumber.Contains("RET-001"));
        Assert.Single(numericSearch.Value.Items, row => row.TotalCost == 80m);
        Assert.Single(adjustmentSearch.Value.Items, row => row.ReferenceNumber == "ADJ-001");
    }

    [Fact]
    public async Task Handle_RespectsTypeFilter()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var from = new DateOnly(2026, 5, 1);
        var to = new DateOnly(2026, 5, 7);

        var saleItem = SaleItem.CreateGoods(shop.Id, Guid.NewGuid(), Guid.NewGuid(), "Item", "BC-001", 1m, 80m, 100m, 100m, 0m, false, false);
        var sale = Sale.Create(shop.Id, "INV-001", null, "Customer", null, PaymentMethod.Cash, new DateTimeOffset(2026, 5, 2, 10, 0, 0, TimeSpan.Zero), 100m, 0m, 100m, 0m, [saleItem]);
        var saleReturn = SaleReturn.Record(
            shop.Id,
            sale.Id,
            "RET-001",
            new DateTimeOffset(2026, 5, 3, 10, 0, 0, TimeSpan.Zero),
            Guid.NewGuid(),
            null,
            100m,
            0m,
            100m,
            PaymentMethod.Cash,
            100m,
            0m,
            null,
            null,
            [new SaleReturnLineInput(shop.Id, saleItem.Id, 1m, SaleReturnCondition.Restockable, 80m, 100m, 0m, false, 100m, 100m, 100m, 0m, null)]).Value;
        var adjustment = InventoryAdjustment.Create(
            shop.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ADJ-001",
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Damaged,
            1m,
            50m,
            50m,
            5m,
            4m,
            5m,
            4m,
            new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero),
            user.Id,
            null,
            user.Id).Value;

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, from, to, Arg.Any<CancellationToken>()).Returns([sale]);
        _saleReturnRepository.GetByShopAndDateRangeAsync(shop.Id, from, to, Arg.Any<CancellationToken>()).Returns([saleReturn]);
        _inventoryAdjustmentRepository.GetByShopAndDateRangeAsync(shop.Id, from, to, Arg.Any<CancellationToken>()).Returns([adjustment]);

        var result = await CreateHandler().Handle(new GetProfitLossReportQuery(user.Id, shop.Id, from, to, Type: "saleReturn"), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(1, result.Value.TotalCount);
        Assert.All(result.Value.Items, row => Assert.Equal(ProfitLossReportRowTypes.SaleReturn, row.RowType));
    }

    [Fact]
    public async Task Handle_ExcludesVoidedReturnsAndVoidedOrIncreasingAdjustments()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var from = new DateOnly(2026, 5, 1);
        var to = new DateOnly(2026, 5, 7);

        var saleItem = SaleItem.CreateGoods(shop.Id, Guid.NewGuid(), Guid.NewGuid(), "Item", "BC-001", 1m, 80m, 100m, 100m, 0m, false, false);
        var sale = Sale.Create(shop.Id, "INV-001", null, "Customer", null, PaymentMethod.Cash, new DateTimeOffset(2026, 5, 2, 10, 0, 0, TimeSpan.Zero), 100m, 0m, 100m, 0m, [saleItem]);
        var activeReturn = SaleReturn.Record(
            shop.Id,
            sale.Id,
            "RET-001",
            new DateTimeOffset(2026, 5, 3, 10, 0, 0, TimeSpan.Zero),
            Guid.NewGuid(),
            null,
            100m,
            0m,
            100m,
            PaymentMethod.Cash,
            100m,
            0m,
            null,
            null,
            [new SaleReturnLineInput(shop.Id, saleItem.Id, 1m, SaleReturnCondition.Restockable, 80m, 100m, 0m, false, 100m, 100m, 100m, 0m, null)]).Value;
        var voidedReturn = SaleReturn.Record(
            shop.Id,
            sale.Id,
            "RET-VOID",
            new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero),
            Guid.NewGuid(),
            null,
            100m,
            0m,
            100m,
            PaymentMethod.Cash,
            100m,
            0m,
            null,
            null,
            [new SaleReturnLineInput(shop.Id, saleItem.Id, 1m, SaleReturnCondition.Restockable, 80m, 100m, 0m, false, 100m, 100m, 100m, 0m, null)]).Value;
        voidedReturn.Void(DateTimeOffset.UtcNow, user.Id, "Mistake");

        var decrease = InventoryAdjustment.Create(shop.Id, Guid.NewGuid(), Guid.NewGuid(), "ADJ-LOSS", InventoryAdjustmentDirection.Decrease, InventoryAdjustmentReason.Damaged, 1m, 40m, 40m, 5m, 4m, 5m, 4m, new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero), user.Id, null, user.Id).Value;
        var increase = InventoryAdjustment.Create(shop.Id, Guid.NewGuid(), Guid.NewGuid(), "ADJ-GAIN", InventoryAdjustmentDirection.Increase, InventoryAdjustmentReason.FoundStock, 1m, 40m, 40m, 5m, 6m, 5m, 6m, new DateTimeOffset(2026, 5, 6, 10, 0, 0, TimeSpan.Zero), user.Id, null, user.Id).Value;
        var voidedDecrease = InventoryAdjustment.Create(shop.Id, Guid.NewGuid(), Guid.NewGuid(), "ADJ-VOID", InventoryAdjustmentDirection.Decrease, InventoryAdjustmentReason.Expired, 1m, 40m, 40m, 5m, 4m, 5m, 4m, new DateTimeOffset(2026, 5, 6, 10, 0, 0, TimeSpan.Zero), user.Id, null, user.Id).Value;
        voidedDecrease.Void(DateTimeOffset.UtcNow, user.Id, "Mistake", Guid.NewGuid());

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, from, to, Arg.Any<CancellationToken>()).Returns([sale]);
        _saleReturnRepository.GetByShopAndDateRangeAsync(shop.Id, from, to, Arg.Any<CancellationToken>()).Returns([activeReturn, voidedReturn]);
        _inventoryAdjustmentRepository.GetByShopAndDateRangeAsync(shop.Id, from, to, Arg.Any<CancellationToken>()).Returns([decrease, increase, voidedDecrease]);

        var result = await CreateHandler().Handle(new GetProfitLossReportQuery(user.Id, shop.Id, from, to), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(3, result.Value.TotalCount);
        Assert.Equal(["ADJ-LOSS", "INV-001 / RET-001", "INV-001"], result.Value.Items.Select(x => x.ReferenceNumber).ToArray());
        Assert.DoesNotContain(result.Value.Items, x => x.ReferenceNumber == "ADJ-GAIN" || x.ReferenceNumber == "ADJ-VOID" || x.ReferenceNumber == "INV-001 / RET-VOID");
        Assert.Equal(1, result.Value.Summary.ReturnCount);
        Assert.Equal(1, result.Value.Summary.AdjustmentCount);
    }

    [Fact]
    public async Task Handle_ReturnsZeroMarginWhenCostIsZero()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var from = new DateOnly(2026, 5, 1);
        var to = new DateOnly(2026, 5, 7);

        var serviceItem = SaleItem.CreateService(shop.Id, Guid.NewGuid(), "Consulting", "SRV-001", 1m, 0m, 150m, 150m, 0m, false, false);
        var sale = Sale.Create(shop.Id, "INV-SRV", null, "Service Customer", null, PaymentMethod.Cash, new DateTimeOffset(2026, 5, 2, 10, 0, 0, TimeSpan.Zero), 150m, 0m, 150m, 0m, [serviceItem]);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, from, to, Arg.Any<CancellationToken>()).Returns([sale]);
        _saleReturnRepository.GetByShopAndDateRangeAsync(shop.Id, from, to, Arg.Any<CancellationToken>()).Returns(Array.Empty<SaleReturn>());
        _inventoryAdjustmentRepository.GetByShopAndDateRangeAsync(shop.Id, from, to, Arg.Any<CancellationToken>()).Returns(Array.Empty<InventoryAdjustment>());

        var result = await CreateHandler().Handle(new GetProfitLossReportQuery(user.Id, shop.Id, from, to), CancellationToken.None);

        Assert.False(result.IsError);
        var row = Assert.Single(result.Value.Items);
        Assert.Equal(0m, row.TotalCost);
        Assert.Null(row.MarginPercent);
        Assert.Null(result.Value.Summary.AverageMarginPercent);
    }
}
