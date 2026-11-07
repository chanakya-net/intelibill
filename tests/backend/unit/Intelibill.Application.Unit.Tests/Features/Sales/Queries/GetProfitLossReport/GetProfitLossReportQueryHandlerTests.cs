using Intelibill.Application.Features.Exports.Accounting;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Queries.GetProfitLossReport;
using Intelibill.Application.Unit.Tests.Features.Exports.Accounting;
using Intelibill.Application.Unit.Tests.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Queries.GetProfitLossReport;

public class GetProfitLossReportQueryHandlerTests
{
    private readonly IExpenseRepository _expenses = Substitute.For<IExpenseRepository>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly ISaleReturnRepository _saleReturnRepository = Substitute.For<ISaleReturnRepository>();
    private readonly IInventoryAdjustmentRepository _inventoryAdjustmentRepository = Substitute.For<IInventoryAdjustmentRepository>();

    private static readonly DateOnly Today = new(2026, 10, 21);
    private readonly MutableTimeProvider _time = MutableTimeProvider.At("2026-10-20T20:30:00+00:00");

    private GetProfitLossReportQueryHandler CreateHandler() =>
        new(
            _userRepository,
            _shopRepository,
            new ProfitLossReportBuilder(
                _saleRepository,
                new ReturnAdjustmentReader(_saleReturnRepository, _saleRepository, Substitute.For<ICreditNoteRepository>()),
                _inventoryAdjustmentRepository,
                BusinessTestDoubles.ClockAt(_time), _expenses),
            BusinessTestDoubles.ClockAt(_time));

    private static User MakeUser() =>
        User.CreateWithEmail("sales@test.com", "hash", "Sales", "User");

    private static Shop MakeShop() =>
        Shop.Create("Test Shop", "123 Main St", "City", "State", "560001", null, null, null);

    private static ShopMembership MakeMembership(Guid shopId, Guid userId) =>
        ShopMembership.Create(shopId, userId, ShopRole.Owner, true);

    [Theory]
    [InlineData(ExpenseTreatment.Operating, 80)]
    [InlineData(ExpenseTreatment.Unclassified, null)]
    public async Task Economics_SeparatesTradingFromClassifiedOperatingCosts(ExpenseTreatment treatment, int? operatingProfit)
    {
        var user = MakeUser(); var shop = MakeShop();
        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(MakeMembership(shop.Id, user.Id));
        var sale = Sale.Create(shop.Id, "ECON-1", null, null, null, PaymentMethod.Cash, DateTimeOffset.UtcNow, 200m, 0m, 200m, 0m,
            [SaleItem.CreateGoods(shop.Id, Guid.NewGuid(), Guid.NewGuid(), "Item", "BC", 1m, 100m, 200m, 200m, 0m, false, false)]);
        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([sale]);
        _expenses.GetByShopAndDateRangeAsync(shop.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([
            Expense.Create(shop.Id, Guid.NewGuid(), 20m, "Landlord", null, Today, user.Id, treatment),
            Expense.CreateFromSupplierPayment(shop.Id, Guid.NewGuid(), 100m, "Supplier", null, Today, user.Id, Guid.NewGuid()),
            Expense.Create(shop.Id, Guid.NewGuid(), 50m, "Equipment", null, Today, user.Id, ExpenseTreatment.CapitalOrOtherExcluded)]);
        var result = await CreateHandler().Handle(new GetProfitLossReportQuery(user.Id, shop.Id), CancellationToken.None);
        Assert.False(result.IsError);
        var economics = Assert.IsType<ProfitLossEconomicsDto>(result.Value.Summary.Economics);
        Assert.Equal(2, economics.Version);
        Assert.Equal(200m, economics.NetSalesExcludingTax);
        Assert.Equal(100m, economics.Cogs);
        Assert.Equal(100m, economics.GrossTradingProfit);
        Assert.Equal(50m, economics.GrossMarginPercent);
        Assert.Equal(100m, economics.MarkupPercent);
        Assert.Equal(operatingProfit.HasValue ? (decimal?)operatingProfit.Value : null, economics.OperatingProfit);
        Assert.Equal(treatment == ExpenseTreatment.Operating ? "Complete" : "UnclassifiedExpenses", economics.OperatingProfitStatus);
    }

    [Fact]
    public async Task Handle_UsesDefaultWindowAndReturnsRowMargin()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var today = Today;

        var item = SaleItem.CreateGoods(shop.Id, Guid.NewGuid(), Guid.NewGuid(), "Item", "BC-001", 1m, 80m, 100m, 100m, 0m, false, false);
        var sale = Sale.Create(shop.Id, "INV-001", null, "John Doe", null, PaymentMethod.Cash, DateTimeOffset.UtcNow, 100m, 0m, 100m, 0m, [item]);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, today.AddDays(-6), today, Arg.Any<CancellationToken>()).Returns([sale]);
        _saleReturnRepository.GetPostedInRangeAsync(shop.Id, today.AddDays(-6), today, Arg.Any<CancellationToken>()).Returns(Array.Empty<SaleReturn>());
        _inventoryAdjustmentRepository.GetPostedInRangeAsync(shop.Id, Arg.Any<UtcDateWindow>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<InventoryAdjustment>());

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
        _saleReturnRepository.GetPostedInRangeAsync(shop.Id, start, end, Arg.Any<CancellationToken>()).Returns(Array.Empty<SaleReturn>());
        _inventoryAdjustmentRepository.GetPostedInRangeAsync(shop.Id, Arg.Any<UtcDateWindow>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<InventoryAdjustment>());

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
            global::Intelibill.Domain.ValueObjects.BusinessDay.InDefaultZone(new DateTimeOffset(2026, 5, 3, 10, 0, 0, TimeSpan.Zero)),
            Guid.NewGuid(),
            null,
            100m,
            0m,
            100m,
            ReturnPayoutDestination.Refund,
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
        _saleReturnRepository.GetPostedInRangeAsync(shop.Id, from, to, Arg.Any<CancellationToken>()).Returns([saleReturn]);
        _inventoryAdjustmentRepository.GetPostedInRangeAsync(shop.Id, Arg.Any<UtcDateWindow>(), Arg.Any<CancellationToken>()).Returns([adjustment]);

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
            global::Intelibill.Domain.ValueObjects.BusinessDay.InDefaultZone(new DateTimeOffset(2026, 5, 3, 10, 0, 0, TimeSpan.Zero)),
            Guid.NewGuid(),
            null,
            100m,
            0m,
            100m,
            ReturnPayoutDestination.Refund,
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
        _saleReturnRepository.GetPostedInRangeAsync(shop.Id, from, to, Arg.Any<CancellationToken>()).Returns([saleReturn]);
        _inventoryAdjustmentRepository.GetPostedInRangeAsync(shop.Id, Arg.Any<UtcDateWindow>(), Arg.Any<CancellationToken>()).Returns([adjustment]);

        var result = await CreateHandler().Handle(new GetProfitLossReportQuery(user.Id, shop.Id, from, to, Type: "saleReturn"), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(1, result.Value.TotalCount);
        Assert.All(result.Value.Items, row => Assert.Equal(ProfitLossReportRowTypes.SaleReturn, row.RowType));
    }

    [Fact]
    public async Task Handle_VoidInPeriod_AddsReversalRow_AndRetainsOriginalLossUntilItsLaterVoid()
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
            global::Intelibill.Domain.ValueObjects.BusinessDay.InDefaultZone(new DateTimeOffset(2026, 5, 3, 10, 0, 0, TimeSpan.Zero)),
            Guid.NewGuid(),
            null,
            100m,
            0m,
            100m,
            ReturnPayoutDestination.Refund,
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
            global::Intelibill.Domain.ValueObjects.BusinessDay.InDefaultZone(new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero)),
            Guid.NewGuid(),
            null,
            100m,
            0m,
            100m,
            ReturnPayoutDestination.Refund,
            100m,
            0m,
            null,
            null,
            [new SaleReturnLineInput(shop.Id, saleItem.Id, 1m, SaleReturnCondition.Restockable, 80m, 100m, 0m, false, 100m, 100m, 100m, 0m, null)]).Value;
        voidedReturn.Void(new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero), global::Intelibill.Domain.Common.BusinessCalendar.DateOf(new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero)), user.Id, "Mistake");

        var decrease = InventoryAdjustment.Create(shop.Id, Guid.NewGuid(), Guid.NewGuid(), "ADJ-LOSS", InventoryAdjustmentDirection.Decrease, InventoryAdjustmentReason.Damaged, 1m, 40m, 40m, 5m, 4m, 5m, 4m, new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero), user.Id, null, user.Id).Value;
        var increase = InventoryAdjustment.Create(shop.Id, Guid.NewGuid(), Guid.NewGuid(), "ADJ-GAIN", InventoryAdjustmentDirection.Increase, InventoryAdjustmentReason.FoundStock, 1m, 40m, 40m, 5m, 6m, 5m, 6m, new DateTimeOffset(2026, 5, 6, 10, 0, 0, TimeSpan.Zero), user.Id, null, user.Id).Value;
        var voidedDecrease = InventoryAdjustment.Create(shop.Id, Guid.NewGuid(), Guid.NewGuid(), "ADJ-VOID", InventoryAdjustmentDirection.Decrease, InventoryAdjustmentReason.Expired, 1m, 40m, 40m, 5m, 4m, 5m, 4m, new DateTimeOffset(2026, 5, 6, 10, 0, 0, TimeSpan.Zero), user.Id, null, user.Id).Value;
        voidedDecrease.Void(DateTimeOffset.UtcNow, user.Id, "Mistake", Guid.NewGuid());

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, from, to, Arg.Any<CancellationToken>()).Returns([sale]);
        _saleReturnRepository.GetPostedInRangeAsync(shop.Id, from, to, Arg.Any<CancellationToken>()).Returns([activeReturn, voidedReturn]);
        _inventoryAdjustmentRepository.GetPostedInRangeAsync(shop.Id, Arg.Any<UtcDateWindow>(), Arg.Any<CancellationToken>()).Returns([decrease, increase, voidedDecrease]);

        var result = await CreateHandler().Handle(new GetProfitLossReportQuery(user.Id, shop.Id, from, to), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(
            ["INV-001 / RET-VOID (void)", "ADJ-VOID", "ADJ-LOSS", "INV-001 / RET-VOID", "INV-001 / RET-001", "INV-001"],
            result.Value.Items.Select(x => x.ReferenceNumber).ToArray());
        Assert.DoesNotContain(result.Value.Items, x => x.ReferenceNumber == "ADJ-GAIN");

        var voidedRows = result.Value.Items.Where(x => x.ReferenceNumber.Contains("RET-VOID", StringComparison.Ordinal)).ToList();
        Assert.Single(voidedRows, x => x.IsReversal);
        Assert.Equal(0m, voidedRows.Sum(x => x.RevenueAfterTax));
        Assert.Equal(0m, voidedRows.Sum(x => x.TotalCost));
        Assert.Equal(0m, voidedRows.Sum(x => x.ProfitAfterTax));
        Assert.Equal(2, result.Value.Summary.ReturnCount);
        Assert.Equal(2, result.Value.Summary.AdjustmentCount);
    }

    [Fact]
    public async Task Handle_ReturnVoidedInLaterPeriod_PostsOnlyTheReversalThere()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var sale = ReturnAccountingFixtures.Sale(shop.Id);
        var saleReturn = ReturnAccountingFixtures.Return(sale, approved: 118m, taxable: 100m, tax: 18m);
        saleReturn.Void(new DateTimeOffset(2026, 7, 2, 9, 0, 0, TimeSpan.Zero), global::Intelibill.Domain.Common.BusinessCalendar.DateOf(new DateTimeOffset(2026, 7, 2, 9, 0, 0, TimeSpan.Zero)), user.Id, "Wrong customer");
        var from = new DateOnly(2026, 7, 1);
        var to = new DateOnly(2026, 7, 31);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, from, to, Arg.Any<CancellationToken>()).Returns(Array.Empty<Sale>());
        _saleRepository.GetByIdsAsync(shop.Id, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([sale]);
        _saleReturnRepository.GetPostedInRangeAsync(shop.Id, from, to, Arg.Any<CancellationToken>()).Returns([saleReturn]);
        _inventoryAdjustmentRepository.GetPostedInRangeAsync(shop.Id, Arg.Any<UtcDateWindow>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<InventoryAdjustment>());

        var result = await CreateHandler().Handle(new GetProfitLossReportQuery(user.Id, shop.Id, from, to), CancellationToken.None);

        Assert.False(result.IsError);
        var row = Assert.Single(result.Value.Items);
        Assert.True(row.IsReversal);
        Assert.Equal("INV-001 / RET-001 (void)", row.ReferenceNumber);
        Assert.Equal(new DateTimeOffset(2026, 7, 2, 9, 0, 0, TimeSpan.Zero), row.OccurredAt);
        Assert.Equal(118m, row.RevenueAfterTax);
        Assert.Equal(100m, row.RevenueBeforeTax);
        Assert.Equal(80m, row.TotalCost);
        Assert.Equal(0, result.Value.Summary.ReturnCount);
    }

    [Fact]
    public async Task Handle_ReturnRevenue_UsesThePostedTaxSplit()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var sale = ReturnAccountingFixtures.Sale(shop.Id);
        var saleReturn = ReturnAccountingFixtures.Return(sale, approved: 100m, taxable: 84.75m, tax: 15.25m);
        var unposted = ReturnAccountingFixtures.Return(sale, approved: 18m, taxable: 0m, tax: 0m, returnNumber: "RET-002", post: false);
        var from = new DateOnly(2026, 6, 1);
        var to = new DateOnly(2026, 6, 30);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, from, to, Arg.Any<CancellationToken>()).Returns([sale]);
        _saleReturnRepository.GetPostedInRangeAsync(shop.Id, from, to, Arg.Any<CancellationToken>()).Returns([saleReturn, unposted]);
        _inventoryAdjustmentRepository.GetPostedInRangeAsync(shop.Id, Arg.Any<UtcDateWindow>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<InventoryAdjustment>());

        var result = await CreateHandler().Handle(new GetProfitLossReportQuery(user.Id, shop.Id, from, to, Type: "saleReturn"), CancellationToken.None);

        Assert.False(result.IsError);
        var posted = Assert.Single(result.Value.Items, x => x.ReferenceNumber == "INV-001 / RET-001");
        Assert.Equal(-100m, posted.RevenueAfterTax);
        Assert.Equal(-84.75m, posted.RevenueBeforeTax);
        Assert.False(posted.NeedsAccountingReview);

        var legacy = Assert.Single(result.Value.Items, x => x.ReferenceNumber == "INV-001 / RET-002");
        Assert.True(legacy.NeedsAccountingReview);
        Assert.Equal(-18m, legacy.RevenueAfterTax);
        Assert.Equal(-18m, legacy.RevenueBeforeTax);
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
        _saleReturnRepository.GetPostedInRangeAsync(shop.Id, from, to, Arg.Any<CancellationToken>()).Returns(Array.Empty<SaleReturn>());
        _inventoryAdjustmentRepository.GetPostedInRangeAsync(shop.Id, Arg.Any<UtcDateWindow>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<InventoryAdjustment>());

        var result = await CreateHandler().Handle(new GetProfitLossReportQuery(user.Id, shop.Id, from, to), CancellationToken.None);

        Assert.False(result.IsError);
        var row = Assert.Single(result.Value.Items);
        Assert.Equal(0m, row.TotalCost);
        Assert.Null(row.MarginPercent);
        Assert.Null(result.Value.Summary.AverageMarginPercent);
        Assert.Equal(100m, result.Value.Summary.Economics!.GrossMarginPercent);
        Assert.Null(result.Value.Summary.Economics.MarkupPercent);
        Assert.Null(result.Value.Summary.Economics.OperatingProfit);
        Assert.Equal("ServiceCostsUnmodeled", result.Value.Summary.Economics.OperatingProfitStatus);
    }
}
