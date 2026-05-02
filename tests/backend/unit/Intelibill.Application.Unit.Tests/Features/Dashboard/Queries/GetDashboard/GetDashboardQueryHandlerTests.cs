using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Dashboard.Queries.GetDashboard;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;
using DomainInventory = Intelibill.Domain.Entities.Inventory;

namespace Intelibill.Application.Unit.Tests.Features.Dashboard.Queries.GetDashboard;

public class GetDashboardQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly IExpenseRepository _expenseRepository = Substitute.For<IExpenseRepository>();
    private readonly IInventoryRepository _inventoryRepository = Substitute.For<IInventoryRepository>();
    private readonly ICustomerRepository _customerRepository = Substitute.For<ICustomerRepository>();
    private readonly ICustomerLedgerEntryRepository _customerLedgerEntryRepository = Substitute.For<ICustomerLedgerEntryRepository>();

    private static DateOnly Today => DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
    private static DateOnly DefaultStart => Today.AddDays(-29);
    private static DateOnly DefaultEnd => Today;

    private GetDashboardQueryHandler CreateHandler() =>
        new(_userRepository, _shopRepository, _saleRepository, _expenseRepository,
            _inventoryRepository, _customerRepository, _customerLedgerEntryRepository);

    private static User MakeUser() =>
        User.CreateWithEmail("dash@test.com", "hash", "Dash", "User");

    private static Shop MakeShop() =>
        Shop.Create("Test Shop", "1 Main St", "City", "State", "560001", null, null, null);

    private static ShopMembership MakeMembership(Guid shopId, Guid userId) =>
        ShopMembership.Create(shopId, userId, ShopRole.Owner, true);

    private static Sale MakeCashSale(Guid shopId, decimal total, decimal paid, decimal tax) =>
        Sale.Create(shopId, "INV-001", null, null, null, PaymentMethod.Cash,
            DateTimeOffset.UtcNow, paid, total - paid, total, tax,
            [SaleItem.Create(shopId, Guid.NewGuid(), Guid.NewGuid(), 1, total * 0.8m, total, total * 1.1m, 10m, false, false)]);

    private static DomainInventory MakeInventory(Guid shopId, decimal quantity, decimal reorderLevel, string itemName = "Widget")
    {
        var itemId = Guid.NewGuid();
        var inventory = DomainInventory.Create(shopId, itemId, quantity, reorderLevel, reorderLevel + 100, Guid.NewGuid()).Value;
        var item = Item.Create(shopId, itemName, null, "pcs", "BC001", true, Guid.NewGuid());
        typeof(DomainInventory).GetProperty("Item")!.SetValue(inventory, item);
        return inventory;
    }

    private void SetupValidUserShopMembership(User user, Shop shop, ShopMembership membership)
    {
        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        _expenseRepository.GetByShopAndDateRangeAsync(shop.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        _inventoryRepository.GetAllByShopWithItemAsync(shop.Id, Arg.Any<CancellationToken>()).Returns([]);
        _customerRepository.GetByShopIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns([]);
        _customerLedgerEntryRepository.GetCustomerBalancesAsync(shop.Id, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal>());
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNotFoundError()
    {
        _userRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), DefaultStart, DefaultEnd), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("User.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenShopNotFound_ReturnsShopNotFoundError()
    {
        var user = MakeUser();
        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Shop?)null);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, Guid.NewGuid(), DefaultStart, DefaultEnd), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.ShopNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenMembershipNotFound_ReturnsMembershipNotFoundError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns((ShopMembership?)null);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, DefaultStart, DefaultEnd), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenStartDateAfterEndDate_ReturnsInvalidDateRangeError()
    {
        var result = await CreateHandler().Handle(
            new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), Today, Today.AddDays(-1)), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Dashboard.InvalidDateRange.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenEndDateInFuture_ReturnsFutureDateNotAllowedError()
    {
        var result = await CreateHandler().Handle(
            new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), Today, Today.AddDays(1)), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Dashboard.FutureDateNotAllowed.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenRangeExceeds90Days_ReturnsRangeExceeds90DaysError()
    {
        var end = Today;
        var start = end.AddDays(-90);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid(), start, end), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Dashboard.RangeExceeds90Days.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenRangeIs89Days_IsAccepted()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var end = Today;
        var start = end.AddDays(-88);
        SetupValidUserShopMembership(user, shop, membership);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, start, end), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(start, result.Value.StartDate);
        Assert.Equal(end, result.Value.EndDate);
    }

    [Fact]
    public async Task Handle_WhenNoData_ReturnsDashboardWithZeroKpis()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupValidUserShopMembership(user, shop, membership);

        var before = DateTimeOffset.UtcNow;
        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, DefaultStart, DefaultEnd), CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        Assert.False(result.IsError);
        var dto = result.Value;
        Assert.Equal(0, dto.SalesCount);
        Assert.Equal(0m, dto.SalesBooked);
        Assert.Equal(0m, dto.CashCollected);
        Assert.Equal(0m, dto.ProfitBeforeTax);
        Assert.Equal(0m, dto.ProfitAfterTax);
        Assert.Equal(0m, dto.ExpenseRecorded);
        Assert.Equal(0m, dto.NetExpense);
        Assert.Equal(0m, dto.CreditSalesAmount);
        Assert.Equal(0m, dto.CreditSalesPercentage);
        Assert.Equal(false, dto.CreditShareWarning);
        Assert.Equal(0, dto.RunningLowStockCount);
        Assert.Equal(0, dto.CriticalStockCount);
        Assert.Empty(dto.RankedShortageList);
        Assert.Null(dto.HighestDueCustomer);
        Assert.NotNull(dto.TopFiveDueCustomers);
        Assert.Empty(dto.TopFiveDueCustomers);
        Assert.True(dto.HasNoSalesActivity);
        Assert.Empty(dto.Alerts);
        Assert.NotNull(dto.SalesTrendSeries);
        Assert.InRange(dto.GeneratedAt, before, after);
    }

    [Fact]
    public async Task Handle_WhenSalesExist_ReturnsSalesKpis()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupValidUserShopMembership(user, shop, membership);

        var sale = MakeCashSale(shop.Id, total: 100m, paid: 100m, tax: 10m);
        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([sale]);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, DefaultStart, DefaultEnd), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(1, result.Value.SalesCount);
        Assert.Equal(100m, result.Value.SalesBooked);
        Assert.Equal(100m, result.Value.CashCollected);
        Assert.Equal(20m, result.Value.ProfitBeforeTax);
        Assert.Equal(10m, result.Value.ProfitAfterTax);
    }

    [Fact]
    public async Task Handle_WhenDateRangeProvided_UsesThatRange()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupValidUserShopMembership(user, shop, membership);

        var start = Today.AddDays(-6);
        var end = Today;

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, start, end), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(start, result.Value.StartDate);
        Assert.Equal(end, result.Value.EndDate);
        await _saleRepository.Received(1).GetByShopAndDateRangeAsync(shop.Id, start, end, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenSalesOnMultipleDays_SalesTrendSeriesHasDailyBuckets()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var start = Today.AddDays(-2);
        var end = Today;
        SetupValidUserShopMembership(user, shop, membership);

        var saleYesterday = Sale.Create(shop.Id, "INV-Y", null, null, null, PaymentMethod.Cash,
            new DateTimeOffset(Today.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            100m, 0m, 100m, 10m,
            [SaleItem.Create(shop.Id, Guid.NewGuid(), Guid.NewGuid(), 1, 80m, 100m, 110m, 10m, false, false)]);
        var saleToday = MakeCashSale(shop.Id, 200m, 200m, 20m);

        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, start, end, Arg.Any<CancellationToken>())
            .Returns([saleYesterday, saleToday]);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, start, end), CancellationToken.None);

        Assert.False(result.IsError);
        var trend = result.Value.SalesTrendSeries!;
        Assert.Equal(3, trend.Count); // 3 days in range
        Assert.Equal(0m, trend[0].Amount); // start (2 days ago) has no sale
        Assert.Equal(100m, trend[1].Amount); // yesterday
        Assert.Equal(200m, trend[2].Amount); // today

        var profitTrend = result.Value.ProfitTrendSeries!;
        Assert.Equal(3, profitTrend.Count);
        Assert.Equal(0m, profitTrend[0].ProfitBeforeTax);
        Assert.Equal(20m, profitTrend[1].ProfitBeforeTax);
        Assert.Equal(10m, profitTrend[1].ProfitAfterTax);
        Assert.Equal(40m, profitTrend[2].ProfitBeforeTax);
        Assert.Equal(20m, profitTrend[2].ProfitAfterTax);

        var paymentMixTrend = result.Value.PaymentMixTrendSeries!;
        Assert.Equal(3, paymentMixTrend.Count);
        Assert.Equal(0m, paymentMixTrend[0].Cash);
        Assert.Equal(100m, paymentMixTrend[1].Cash);
        Assert.Equal(200m, paymentMixTrend[2].Cash);
        Assert.Equal(0m, paymentMixTrend[0].Credit);
        Assert.Equal(0m, paymentMixTrend[1].Credit);
        Assert.Equal(0m, paymentMixTrend[2].Credit);
    }

    [Fact]
    public async Task Handle_WhenStaffRole_SalesTrendSeriesIsNull()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var staffMembership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Staff, true);
        SetupValidUserShopMembership(user, shop, staffMembership);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, DefaultStart, DefaultEnd), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Null(result.Value.SalesTrendSeries);
        Assert.Null(result.Value.PaymentMixTrendSeries);
    }

    [Fact]
    public async Task Handle_WhenCreditSalesExceedThreshold_SetsCreditShareWarning()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupValidUserShopMembership(user, shop, membership);

        var creditSale = Sale.Create(shop.Id, "INV-002", null, null, null, PaymentMethod.Credit,
            DateTimeOffset.UtcNow, 0m, 50m, 50m, 5m,
            [SaleItem.Create(shop.Id, Guid.NewGuid(), Guid.NewGuid(), 1, 30m, 50m, 60m, 10m, false, false)]);
        var cashSale = MakeCashSale(shop.Id, 50m, 50m, 5m);

        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([creditSale, cashSale]);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, DefaultStart, DefaultEnd), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(true, result.Value.CreditShareWarning);
        Assert.Equal(50m, result.Value.CreditSalesAmount);
        Assert.Equal(0.5m, result.Value.CreditSalesPercentage);
    }

    [Fact]
    public async Task Handle_WhenCreditSalesBelowThreshold_DoesNotSetCreditShareWarning()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupValidUserShopMembership(user, shop, membership);

        var creditSale = Sale.Create(shop.Id, "INV-002", null, null, null, PaymentMethod.Credit,
            DateTimeOffset.UtcNow, 0m, 10m, 10m, 1m,
            [SaleItem.Create(shop.Id, Guid.NewGuid(), Guid.NewGuid(), 1, 6m, 10m, 12m, 10m, false, false)]);
        var cashSale = MakeCashSale(shop.Id, 90m, 90m, 9m);

        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([creditSale, cashSale]);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, DefaultStart, DefaultEnd), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(false, result.Value.CreditShareWarning);
    }

    [Fact]
    public async Task Handle_WhenInventoryBelowReorderLevel_SetsRunningLowStock()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupValidUserShopMembership(user, shop, membership);

        var lowInventory = MakeInventory(shop.Id, quantity: 2m, reorderLevel: 10m, "Widget A");
        var criticalInventory = MakeInventory(shop.Id, quantity: 0m, reorderLevel: 5m, "Widget B");
        var okInventory = MakeInventory(shop.Id, quantity: 20m, reorderLevel: 5m, "Widget C");

        _inventoryRepository.GetAllByShopWithItemAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns([lowInventory, criticalInventory, okInventory]);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, DefaultStart, DefaultEnd), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(1, result.Value.RunningLowStockCount);
        Assert.Equal(1, result.Value.CriticalStockCount);
        Assert.Equal(2, result.Value.RankedShortageList.Count);
    }

    [Fact]
    public async Task Handle_WhenCustomersHaveDues_ReturnsTopFiveAndHighest()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupValidUserShopMembership(user, shop, membership);

        var customers = Enumerable.Range(1, 6)
            .Select(i => Customer.Create(shop.Id, $"Customer {i}", $"9000{i:D6}", null))
            .ToList();

        _customerRepository.GetByShopIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(customers);

        var balances = customers
            .Select((c, idx) => (c.Id, Amount: (idx + 1) * 100m))
            .ToDictionary(x => x.Id, x => x.Amount);

        _customerLedgerEntryRepository.GetCustomerBalancesAsync(
                shop.Id, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(balances);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, DefaultStart, DefaultEnd), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(result.Value.HighestDueCustomer);
        Assert.Equal(600m, result.Value.HighestDueCustomer!.OutstandingDue);
        Assert.Equal(5, result.Value.TopFiveDueCustomers!.Count);
    }

    [Fact]
    public async Task Handle_WhenExpensesExist_ReturnsExpenseKpis()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupValidUserShopMembership(user, shop, membership);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var categoryId = Guid.NewGuid();

        var original = Expense.Create(shop.Id, categoryId, 200m, "Vendor A", null, today, user.Id);
        var correction = Expense.CreateCorrection(shop.Id, categoryId, -50m, "Vendor A", "Correction", today, user.Id, original.Id);

        _expenseRepository.GetByShopAndDateRangeAsync(shop.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([original, correction]);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, DefaultStart, DefaultEnd), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(200m, result.Value.ExpenseRecorded);
        Assert.Equal(-50m, result.Value.ExpenseCorrection);
        Assert.Equal(150m, result.Value.NetExpense);
    }

    [Fact]
    public async Task Handle_WhenPaymentMethodsMixed_ReturnsCorrectPaymentMix()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupValidUserShopMembership(user, shop, membership);

        var cashSale = MakeCashSale(shop.Id, 100m, 100m, 10m);
        var upiSale = Sale.Create(shop.Id, "INV-U", null, null, null, PaymentMethod.UPI,
            DateTimeOffset.UtcNow, 200m, 0m, 200m, 20m,
            [SaleItem.Create(shop.Id, Guid.NewGuid(), Guid.NewGuid(), 1, 150m, 200m, 220m, 10m, false, false)]);
        var cardSale = Sale.Create(shop.Id, "INV-C", null, null, null, PaymentMethod.Card,
            DateTimeOffset.UtcNow, 300m, 0m, 300m, 30m,
            [SaleItem.Create(shop.Id, Guid.NewGuid(), Guid.NewGuid(), 1, 250m, 300m, 330m, 10m, false, false)]);

        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([cashSale, upiSale, cardSale]);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, DefaultStart, DefaultEnd), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(100m, result.Value.PaymentMix!.Cash);
        Assert.Equal(200m, result.Value.PaymentMix.Upi);
        Assert.Equal(300m, result.Value.PaymentMix.Card);
        Assert.Equal(0m, result.Value.PaymentMix.Credit);
    }

    [Fact]
    public async Task Handle_WhenCashSaleHasDueAmount_AllocatesDueToCreditMetrics()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupValidUserShopMembership(user, shop, membership);

        var partialCashSale = MakeCashSale(shop.Id, total: 100m, paid: 40m, tax: 10m);
        var upiSale = Sale.Create(shop.Id, "INV-U", null, null, null, PaymentMethod.UPI,
            DateTimeOffset.UtcNow, 100m, 0m, 100m, 10m,
            [SaleItem.Create(shop.Id, Guid.NewGuid(), Guid.NewGuid(), 1, 70m, 100m, 110m, 10m, false, false)]);

        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([partialCashSale, upiSale]);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, DefaultStart, DefaultEnd), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(60m, result.Value.CreditSalesAmount);
        Assert.Equal(0.3m, result.Value.CreditSalesPercentage);
        Assert.Equal(40m, result.Value.PaymentMix!.Cash);
        Assert.Equal(100m, result.Value.PaymentMix.Upi);
        Assert.Equal(0m, result.Value.PaymentMix.Card);
        Assert.Equal(60m, result.Value.PaymentMix.Credit);
    }

    [Fact]
    public async Task Handle_WhenStaffRole_NullsOutFinancialKpis()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var staffMembership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Staff, true);
        SetupValidUserShopMembership(user, shop, staffMembership);

        var sale = MakeCashSale(shop.Id, 100m, 100m, 10m);
        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([sale]);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, DefaultStart, DefaultEnd), CancellationToken.None);

        Assert.False(result.IsError);
        var dto = result.Value;
        Assert.Equal(1, dto.SalesCount);
        Assert.Null(dto.SalesBooked);
        Assert.Null(dto.CashCollected);
        Assert.Null(dto.ProfitBeforeTax);
        Assert.Null(dto.ProfitAfterTax);
        Assert.Null(dto.ExpenseRecorded);
        Assert.Null(dto.ExpenseCorrection);
        Assert.Null(dto.NetExpense);
        Assert.Null(dto.CreditSalesAmount);
        Assert.Null(dto.CreditSalesPercentage);
        Assert.Null(dto.PaymentMix);
        Assert.Null(dto.CreditShareWarning);
        Assert.Null(dto.HighestDueCustomer);
        Assert.Null(dto.TopFiveDueCustomers);
    }

    [Fact]
    public async Task Handle_WhenStaffRole_StillReturnsStockKpis()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var staffMembership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Staff, true);
        SetupValidUserShopMembership(user, shop, staffMembership);

        var lowInventory = MakeInventory(shop.Id, quantity: 2m, reorderLevel: 10m, "Widget A");
        _inventoryRepository.GetAllByShopWithItemAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns([lowInventory]);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, DefaultStart, DefaultEnd), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(1, result.Value.RunningLowStockCount);
        Assert.Equal(0, result.Value.CriticalStockCount);
        Assert.Single(result.Value.RankedShortageList);
    }

    [Fact]
    public async Task Handle_WhenSalesExist_HasNoSalesActivityIsFalse()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupValidUserShopMembership(user, shop, membership);

        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([MakeCashSale(shop.Id, 100m, 100m, 10m)]);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, DefaultStart, DefaultEnd), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.False(result.Value.HasNoSalesActivity);
    }

    [Fact]
    public async Task Handle_WhenCriticalStockExists_AddsAlertWithPriorityOne()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupValidUserShopMembership(user, shop, membership);

        var criticalInventory = MakeInventory(shop.Id, quantity: 0m, reorderLevel: 5m, "Widget X");
        _inventoryRepository.GetAllByShopWithItemAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns([criticalInventory]);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, DefaultStart, DefaultEnd), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains(result.Value.Alerts, a => a.AlertType == "CriticalStock" && a.Priority == 1);
    }

    [Fact]
    public async Task Handle_AlertsOrderedByPriorityWithFinancialAlertsForOwner()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupValidUserShopMembership(user, shop, membership);

        var criticalInventory = MakeInventory(shop.Id, quantity: 0m, reorderLevel: 5m, "Widget X");
        var lowInventory = MakeInventory(shop.Id, quantity: 2m, reorderLevel: 10m, "Widget Y");
        _inventoryRepository.GetAllByShopWithItemAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns([criticalInventory, lowInventory]);

        var creditSale = Sale.Create(shop.Id, "INV-002", null, null, null, PaymentMethod.Credit,
            DateTimeOffset.UtcNow, 0m, 50m, 50m, 5m,
            [SaleItem.Create(shop.Id, Guid.NewGuid(), Guid.NewGuid(), 1, 30m, 50m, 60m, 10m, false, false)]);
        var cashSale = MakeCashSale(shop.Id, 50m, 50m, 5m);
        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([creditSale, cashSale]);

        var customer = Customer.Create(shop.Id, "Big Buyer", "9000000001", null);
        _customerRepository.GetByShopIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns([customer]);
        _customerLedgerEntryRepository.GetCustomerBalancesAsync(
                shop.Id, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal> { [customer.Id] = 1000m });

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, DefaultStart, DefaultEnd), CancellationToken.None);

        Assert.False(result.IsError);
        var alerts = result.Value.Alerts;
        Assert.Equal(4, alerts.Count);
        Assert.Equal("CriticalStock", alerts[0].AlertType);
        Assert.Equal("HighestDue", alerts[1].AlertType);
        Assert.Equal("RunningLowStock", alerts[2].AlertType);
        Assert.Equal("CreditShareWarning", alerts[3].AlertType);
    }

    [Fact]
    public async Task Handle_WhenStaffRole_FinancialAlertsOmitted()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var staffMembership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Staff, true);
        SetupValidUserShopMembership(user, shop, staffMembership);

        var criticalInventory = MakeInventory(shop.Id, quantity: 0m, reorderLevel: 5m, "Widget X");
        _inventoryRepository.GetAllByShopWithItemAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns([criticalInventory]);

        var creditSale = Sale.Create(shop.Id, "INV-002", null, null, null, PaymentMethod.Credit,
            DateTimeOffset.UtcNow, 0m, 50m, 50m, 5m,
            [SaleItem.Create(shop.Id, Guid.NewGuid(), Guid.NewGuid(), 1, 30m, 50m, 60m, 10m, false, false)]);
        var cashSale = MakeCashSale(shop.Id, 50m, 50m, 5m);
        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([creditSale, cashSale]);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, DefaultStart, DefaultEnd), CancellationToken.None);

        Assert.False(result.IsError);
        var alertTypes = result.Value.Alerts.Select(a => a.AlertType).ToList();
        Assert.Contains("CriticalStock", alertTypes);
        Assert.DoesNotContain("HighestDue", alertTypes);
        Assert.DoesNotContain("CreditShareWarning", alertTypes);
    }

    [Fact]
    public async Task Handle_WhenOwnerRole_ProfitTrendSeriesHasDailyBuckets()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var start = Today.AddDays(-1);
        var end = Today;
        SetupValidUserShopMembership(user, shop, membership);

        var saleYesterday = Sale.Create(shop.Id, "INV-PT1", null, null, null, PaymentMethod.Cash,
            new DateTimeOffset(Today.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            100m, 0m, 100m, 10m,
            [SaleItem.Create(shop.Id, Guid.NewGuid(), Guid.NewGuid(), 1, 60m, 100m, 110m, 10m, false, false)]);

        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, start, end, Arg.Any<CancellationToken>())
            .Returns([saleYesterday]);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, start, end), CancellationToken.None);

        Assert.False(result.IsError);
        var trend = result.Value.ProfitTrendSeries!;
        Assert.Equal(2, trend.Count); // 2 days: yesterday + today
        Assert.Equal(40m, trend[0].ProfitBeforeTax); // 100 - 60 = 40
        Assert.Equal(30m, trend[0].ProfitAfterTax); // 100 - 10 - 60 = 30
        Assert.Equal(0m, trend[1].ProfitAfterTax); // today: no sales
    }

    [Fact]
    public async Task Handle_WhenStaffRole_ProfitTrendSeriesIsNull()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var staffMembership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Staff, true);
        SetupValidUserShopMembership(user, shop, staffMembership);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, DefaultStart, DefaultEnd), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Null(result.Value.ProfitTrendSeries);
    }

    [Fact]
    public async Task Handle_WhenOwnerRole_PreviousPeriodSummaryIsComputedFromPrecedingEqualSpan()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var start = Today.AddDays(-6); // 7-day range (spanDays = 6)
        var end = Today;
        SetupValidUserShopMembership(user, shop, membership);

        var spanDays = end.DayNumber - start.DayNumber;
        var prevEnd = start.AddDays(-1);
        var prevStart = prevEnd.AddDays(-spanDays);

        var prevSale = Sale.Create(shop.Id, "INV-PREV1", null, null, null, PaymentMethod.Cash,
            new DateTimeOffset(prevEnd.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            100m, 0m, 100m, 0m,
            [SaleItem.Create(shop.Id, Guid.NewGuid(), Guid.NewGuid(), 1, 60m, 100m, 100m, 0m, false, false)]);

        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, prevStart, prevEnd, Arg.Any<CancellationToken>())
            .Returns([prevSale]);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, start, end), CancellationToken.None);

        Assert.False(result.IsError);
        var prev = result.Value.PreviousPeriodSummary!;
        Assert.NotNull(prev);
        Assert.Equal(prevStart, prev.StartDate);
        Assert.Equal(prevEnd, prev.EndDate);
        Assert.Equal(1, prev.SalesCount);
        Assert.Equal(100m, prev.SalesBooked);
        Assert.Equal(40m, prev.ProfitAfterTax); // 100 - 60 = 40
    }

    [Fact]
    public async Task Handle_WhenPreviousPeriodSalesHaveTaxAndDue_UsesAfterTaxProfitAndDueBasedCreditShare()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var start = Today.AddDays(-6);
        var end = Today;
        SetupValidUserShopMembership(user, shop, membership);

        var spanDays = end.DayNumber - start.DayNumber;
        var prevEnd = start.AddDays(-1);
        var prevStart = prevEnd.AddDays(-spanDays);

        var prevPartialCashSale = Sale.Create(shop.Id, "INV-PREV-CASH", null, null, null, PaymentMethod.Cash,
            new DateTimeOffset(prevEnd.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            40m, 60m, 100m, 10m,
            [SaleItem.Create(shop.Id, Guid.NewGuid(), Guid.NewGuid(), 1, 70m, 100m, 110m, 10m, false, false)]);
        var prevUpiSale = Sale.Create(shop.Id, "INV-PREV-UPI", null, null, null, PaymentMethod.UPI,
            new DateTimeOffset(prevEnd.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            100m, 0m, 100m, 10m,
            [SaleItem.Create(shop.Id, Guid.NewGuid(), Guid.NewGuid(), 1, 60m, 100m, 110m, 10m, false, false)]);

        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, prevStart, prevEnd, Arg.Any<CancellationToken>())
            .Returns([prevPartialCashSale, prevUpiSale]);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, start, end), CancellationToken.None);

        Assert.False(result.IsError);
        var prev = result.Value.PreviousPeriodSummary;
        Assert.NotNull(prev);
        Assert.Equal(60m, prev.CreditSalesPercentage * prev.SalesBooked);
        Assert.Equal(50m, prev.ProfitAfterTax);
    }

    [Fact]
    public async Task Handle_WhenStaffRole_PreviousPeriodSummaryIsNull()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var staffMembership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Staff, true);
        SetupValidUserShopMembership(user, shop, staffMembership);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id, DefaultStart, DefaultEnd), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Null(result.Value.PreviousPeriodSummary);
    }
}
