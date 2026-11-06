using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Customers.Queries.GetCustomerAccount;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;
using Intelibill.Application.Unit.Tests.Common.Time;
using Intelibill.Infrastructure.Services.Time;
using Intelibill.Application.Features.Payments.DTOs;
using Intelibill.Application.Features.Payments.Services;

namespace Intelibill.Application.Unit.Tests.Features.Customers.Queries.GetCustomerAccount;

public class GetCustomerAccountQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ICustomerRepository _customerRepository = Substitute.For<ICustomerRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly ICustomerLedgerEntryRepository _customerLedgerEntryRepository = Substitute.For<ICustomerLedgerEntryRepository>();

    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly MutableTimeProvider _time = new();
    private readonly IInvoiceBalanceReader _balanceReader = Substitute.For<IInvoiceBalanceReader>();

    public GetCustomerAccountQueryHandlerTests()
    {
        _shopRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Shop?>(Shop.Create("Main", "Address", "City", "State", "560001", null, null, null)));
        _balanceReader.GetForCustomerAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerInvoiceBalanceDto>());
        _balanceReader.GetReceiptsAsync(Arg.Any<Guid>(), Arg.Any<PaymentPartyType>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PaymentReceiptSummaryDto>());
    }

    private GetCustomerAccountQueryHandler CreateHandler() =>
        new(_userRepository, _customerRepository, _saleRepository, _customerLedgerEntryRepository, _balanceReader,
            _shopRepository, new BusinessClock(_time));

    private static (User owner, User manager, User staff, Shop shop, Customer customer) BuildFixture()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var manager = User.CreateWithEmail("manager@test.com", "hash", "Manager", "User");
        var staff = User.CreateWithEmail("staff@test.com", "hash", "Staff", "User");

        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var ownerMembership = ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true);
        var managerMembership = ShopMembership.Create(shop.Id, manager.Id, ShopRole.Manager, false);
        var staffMembership = ShopMembership.Create(shop.Id, staff.Id, ShopRole.Staff, false);

        owner.AddShopMembership(ownerMembership);
        manager.AddShopMembership(managerMembership);
        staff.AddShopMembership(staffMembership);
        shop.AddMembership(ownerMembership);
        shop.AddMembership(managerMembership);
        shop.AddMembership(staffMembership);

        var customer = Customer.Create(shop.Id, "Customer A", "+919000000001", null, true);

        return (owner, manager, staff, shop, customer);
    }

    [Fact]
    public async Task HandleAsync_ManagerRole_ReturnsAccountDtoWithSortedLedger()
    {
        var fixture = BuildFixture();
        var query = new GetCustomerAccountQuery(fixture.manager.Id, fixture.shop.Id, fixture.customer.Id);

        var sale = Sale.Create(
            fixture.shop.Id,
            "INV-001",
            fixture.customer.Id,
            fixture.customer.Name,
            fixture.customer.PhoneNumber,
            PaymentMethod.Credit,
            DateTimeOffset.UtcNow,
            80m,
            20m,
            100m,
            0m,
            [
                SaleItem.CreateGoods(
                    fixture.shop.Id,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    lineName: "Item",
                    lineCode: "BC-001",
                    1m,
                    80m,
                    100m,
                    120m,
                    0m,
                    false,
                    false,
                    totalAmount: 100m)
            ]);

        var dueEntry = CustomerLedgerEntry.Create(
            fixture.shop.Id,
            fixture.customer.Id,
            sale.Id,
            CustomerLedgerEntryType.SaleDue,
            20m,
            new DateOnly(2026, 4, 20),
            "Due from sale",
            fixture.owner.Id).Value;

        var paymentEntry = CustomerLedgerEntry.Create(
            fixture.shop.Id,
            fixture.customer.Id,
            null,
            CustomerLedgerEntryType.PaymentReceived,
            5m,
            new DateOnly(2026, 4, 22),
            "Part payment",
            fixture.manager.Id).Value;

        var returnCreditEntry = CustomerLedgerEntry.Create(
            fixture.shop.Id,
            fixture.customer.Id,
            sale.Id,
            CustomerLedgerEntryType.ReturnCredit,
            3m,
            new DateOnly(2026, 4, 21),
            "Return credit",
            fixture.manager.Id).Value;

        var returnCreditReversalEntry = CustomerLedgerEntry.Create(
            fixture.shop.Id,
            fixture.customer.Id,
            sale.Id,
            CustomerLedgerEntryType.ReturnCreditReversal,
            2m,
            new DateOnly(2026, 4, 23),
            "Return reversal",
            fixture.manager.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(fixture.manager.Id, Arg.Any<CancellationToken>()).Returns(fixture.manager);
        _customerRepository.GetByShopAndIdAsync(fixture.shop.Id, fixture.customer.Id, Arg.Any<CancellationToken>()).Returns(fixture.customer);
        _saleRepository.GetByCustomerAsync(fixture.shop.Id, fixture.customer.Id, Arg.Any<CancellationToken>()).Returns([sale]);
        _customerLedgerEntryRepository.GetByCustomerAsync(fixture.shop.Id, fixture.customer.Id, Arg.Any<CancellationToken>())
            .Returns([dueEntry, paymentEntry, returnCreditEntry, returnCreditReversalEntry]);
        _customerLedgerEntryRepository.GetCustomerBalanceAsync(fixture.shop.Id, fixture.customer.Id, Arg.Any<CancellationToken>())
            .Returns(14m);

        var result = await CreateHandler().HandleAsync(query, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(14m, result.Value.OutstandingDue);
        Assert.Single(result.Value.Sales);
        Assert.Equal(returnCreditReversalEntry.Id, result.Value.LedgerEntries[0].EntryId);
        Assert.Equal(CustomerLedgerEntryType.ReturnCreditReversal, result.Value.LedgerEntries[0].EntryType);
        Assert.Equal(sale.Id, result.Value.LedgerEntries[0].SaleId);
        Assert.Equal(14m, result.Value.LedgerEntries[0].RunningBalance);
        Assert.Equal(paymentEntry.Id, result.Value.LedgerEntries[1].EntryId);
        Assert.Equal(CustomerLedgerEntryType.PaymentReceived, result.Value.LedgerEntries[1].EntryType);
        Assert.Equal(12m, result.Value.LedgerEntries[1].RunningBalance);
        Assert.Equal(returnCreditEntry.Id, result.Value.LedgerEntries[2].EntryId);
        Assert.Equal(CustomerLedgerEntryType.ReturnCredit, result.Value.LedgerEntries[2].EntryType);
        Assert.Equal(sale.Id, result.Value.LedgerEntries[2].SaleId);
        Assert.Equal(17m, result.Value.LedgerEntries[2].RunningBalance);
        Assert.Equal(CustomerLedgerEntryType.SaleDue, result.Value.LedgerEntries[3].EntryType);
        Assert.Single(result.Value.PaymentHistory);
        Assert.Equal(paymentEntry.Id, result.Value.PaymentHistory[0].EntryId);
        Assert.Equal(100m, result.Value.Sales[0].TotalAmount);
    }

    [Fact]
    public async Task HandleAsync_ManagerRole_ReturnsDiscountedSaleTotal()
    {
        var fixture = BuildFixture();
        var query = new GetCustomerAccountQuery(fixture.manager.Id, fixture.shop.Id, fixture.customer.Id);

        var discountedItem = SaleItem.CreateGoods(
            fixture.shop.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            lineName: "Item",
            lineCode: "BC-001",
            1m,
            50m,
            100m,
            120m,
            0m,
            false,
            false,
            finalSalesPrice: 80m,
            itemDiscountAmount: 20m,
            taxableAmount: 80m,
            taxAmount: 0m,
            totalAmount: 80m);

        var sale = Sale.Create(
            fixture.shop.Id,
            "INV-DISC",
            fixture.customer.Id,
            fixture.customer.Name,
            fixture.customer.PhoneNumber,
            PaymentMethod.Credit,
            DateTimeOffset.UtcNow,
            80m,
            20m,
            100m,
            0m,
            [discountedItem]);

        _userRepository.GetByIdWithDetailsAsync(fixture.manager.Id, Arg.Any<CancellationToken>()).Returns(fixture.manager);
        _customerRepository.GetByShopAndIdAsync(fixture.shop.Id, fixture.customer.Id, Arg.Any<CancellationToken>()).Returns(fixture.customer);
        _saleRepository.GetByCustomerAsync(fixture.shop.Id, fixture.customer.Id, Arg.Any<CancellationToken>()).Returns([sale]);
        _customerLedgerEntryRepository.GetByCustomerAsync(fixture.shop.Id, fixture.customer.Id, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerLedgerEntry>());
        _customerLedgerEntryRepository.GetCustomerBalanceAsync(fixture.shop.Id, fixture.customer.Id, Arg.Any<CancellationToken>())
            .Returns(20m);

        var result = await CreateHandler().HandleAsync(query, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value.Sales);
        Assert.Equal(80m, result.Value.Sales[0].TotalAmount);
    }

    [Fact]
    public async Task HandleAsync_StaffRole_ReturnsForbidden()
    {
        var fixture = BuildFixture();
        var query = new GetCustomerAccountQuery(fixture.staff.Id, fixture.shop.Id, fixture.customer.Id);

        _userRepository.GetByIdWithDetailsAsync(fixture.staff.Id, Arg.Any<CancellationToken>()).Returns(fixture.staff);

        var result = await CreateHandler().HandleAsync(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Customer.UserIsNotOwnerOrManager.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_ActorNotFound_ReturnsUserNotFound()
    {
        var fixture = BuildFixture();
        var query = new GetCustomerAccountQuery(Guid.NewGuid(), fixture.shop.Id, fixture.customer.Id);

        _userRepository.GetByIdWithDetailsAsync(query.UserId, Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await CreateHandler().HandleAsync(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.UserNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_MembershipMissing_ReturnsMembershipNotFound()
    {
        var fixture = BuildFixture();
        var outsider = User.CreateWithEmail("outsider@test.com", "hash", "Out", "Sider");
        var query = new GetCustomerAccountQuery(outsider.Id, fixture.shop.Id, fixture.customer.Id);

        _userRepository.GetByIdWithDetailsAsync(outsider.Id, Arg.Any<CancellationToken>())
            .Returns(outsider);

        var result = await CreateHandler().HandleAsync(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_CustomerNotFound_ReturnsCustomerNotFound()
    {
        var fixture = BuildFixture();
        var query = new GetCustomerAccountQuery(fixture.manager.Id, fixture.shop.Id, Guid.NewGuid());

        _userRepository.GetByIdWithDetailsAsync(fixture.manager.Id, Arg.Any<CancellationToken>()).Returns(fixture.manager);
        _customerRepository.GetByShopAndIdAsync(fixture.shop.Id, query.CustomerId, Arg.Any<CancellationToken>())
            .Returns((Customer?)null);

        var result = await CreateHandler().HandleAsync(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Customer.CustomerNotFound.Code, result.FirstError.Code);
    }

    private static Sale DueSale(Guid shopId, Customer customer, decimal due, DateTimeOffset soldAt, string invoice = "INV-100") =>
        Sale.Create(
            shopId,
            invoice,
            customer.Id,
            customer.Name,
            customer.PhoneNumber,
            PaymentMethod.Credit,
            soldAt,
            0m,
            due,
            due,
            0m,
            [
                SaleItem.CreateGoods(shopId, Guid.NewGuid(), Guid.NewGuid(), lineName: "Item", lineCode: "BC-001", 1m, 1m, due, due, 0m, false, false, totalAmount: due)
            ]);

    private static CustomerLedgerEntry Entry(Guid shopId, Guid customerId, CustomerLedgerEntryType type, decimal amount, DateOnly date, Guid? saleId = null) =>
        CustomerLedgerEntry.Create(
            shopId,
            customerId,
            saleId ?? (type is CustomerLedgerEntryType.PaymentReceived or CustomerLedgerEntryType.PaymentReversal ? null : Guid.NewGuid()),
            type,
            amount,
            date,
            null,
            Guid.NewGuid()).Value;

    private void Arrange(
        (User owner, User manager, User staff, Shop shop, Customer customer) fixture,
        IReadOnlyList<Sale> sales,
        IReadOnlyList<CustomerLedgerEntry> entries,
        IReadOnlyList<CustomerInvoiceBalanceDto>? balances = null)
    {
        _userRepository.GetByIdWithDetailsAsync(fixture.owner.Id, Arg.Any<CancellationToken>()).Returns(fixture.owner);
        _shopRepository.GetByIdAsync(fixture.shop.Id, Arg.Any<CancellationToken>()).Returns(fixture.shop);
        _customerRepository.GetByShopAndIdAsync(fixture.shop.Id, fixture.customer.Id, Arg.Any<CancellationToken>()).Returns(fixture.customer);
        _saleRepository.GetByCustomerAsync(fixture.shop.Id, fixture.customer.Id, Arg.Any<CancellationToken>()).Returns(sales);
        _customerLedgerEntryRepository.GetByCustomerAsync(fixture.shop.Id, fixture.customer.Id, Arg.Any<CancellationToken>()).Returns(entries);
        _customerLedgerEntryRepository.GetCustomerBalanceAsync(fixture.shop.Id, fixture.customer.Id, Arg.Any<CancellationToken>())
            .Returns(entries.Sum(e => e.EntryType.ToBalanceDelta(e.Amount)));
        if (balances is not null)
        {
            _balanceReader.GetForCustomerAsync(fixture.shop.Id, fixture.customer.Id, null, Arg.Any<CancellationToken>()).Returns(balances);
        }
    }

    [Fact]
    public async Task Without_an_as_of_date_the_account_is_judged_today_in_the_shops_zone()
    {
        // 20:30 UTC on 6 Nov is already 7 Nov in Kolkata but still 6 Nov in New York.
        _time.Now = DateTimeOffset.Parse("2026-11-06T20:30:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var fixture = BuildFixture();
        typeof(Shop).GetProperty(nameof(Shop.BusinessTimeZoneId))!.SetValue(fixture.shop, "America/New_York");
        Arrange(fixture, [], []);

        var result = await CreateHandler().HandleAsync(
            new GetCustomerAccountQuery(fixture.owner.Id, fixture.shop.Id, fixture.customer.Id), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 11, 6), result.Value.AsOfBusinessDate);
    }

    [Fact]
    public async Task Account_returns_saved_limit_not_lifetime_revenue()
    {
        // RF1: limit 2,000, lifetime revenue 20,000, balance 500 → exactly 2,000 limit, 1,500 available.
        var fixture = BuildFixture();
        var customer = Customer.Create(fixture.shop.Id, "Customer A", "+919000000001", null, true, 2000m);
        Assert.False(customer.SetCreditPolicy(CreditPolicyMode.Enforced, null).IsError);
        fixture.customer = customer;
        var bigSale = Sale.Create(fixture.shop.Id, "INV-BIG", customer.Id, customer.Name, customer.PhoneNumber, PaymentMethod.Cash, DateTimeOffset.UtcNow, 19500m, 500m, 20000m, 0m,
            [SaleItem.CreateGoods(fixture.shop.Id, Guid.NewGuid(), Guid.NewGuid(), lineName: "Item", lineCode: "BC-001", 1m, 1m, 20000m, 20000m, 0m, false, false, totalAmount: 20000m)]);
        Arrange(fixture, [bigSale], [Entry(fixture.shop.Id, customer.Id, CustomerLedgerEntryType.SaleDue, 500m, new DateOnly(2026, 10, 1), bigSale.Id)]);

        var result = await CreateHandler().HandleAsync(new GetCustomerAccountQuery(fixture.owner.Id, fixture.shop.Id, customer.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2000m, result.Value.CreditLimit);
        Assert.Equal(CreditPolicyMode.Enforced, result.Value.CreditPolicyMode);
        Assert.Equal(1500m, result.Value.AvailableCredit);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task Legacy_advisory_customer_has_no_enforced_available_credit()
    {
        var fixture = BuildFixture();
        Arrange(fixture, [], []);

        var result = await CreateHandler().HandleAsync(new GetCustomerAccountQuery(fixture.owner.Id, fixture.shop.Id, fixture.customer.Id), CancellationToken.None);

        Assert.Equal(CreditPolicyMode.LegacyAdvisory, result.Value.CreditPolicyMode);
        Assert.Null(result.Value.AvailableCredit);
    }

    [Theory]
    [InlineData(null, false)]   // RF2: no due date → outstanding, never overdue
    [InlineData(5, false)]      // due date in the future
    [InlineData(0, false)]      // due today is not yet overdue
    [InlineData(-1, true)]      // due date passed
    public async Task Overdue_is_a_due_date_fact(int? dueInDays, bool expectedOverdue)
    {
        var fixture = BuildFixture();
        var asOf = new DateOnly(2026, 11, 6);
        var customer = Customer.Create(fixture.shop.Id, "Customer A", "+919000000001", null, true, 1000m);
        Assert.False(customer.SetCreditPolicy(CreditPolicyMode.Enforced, dueInDays is null ? null : 10).IsError);
        fixture.customer = customer;
        var sale = DueSale(fixture.shop.Id, customer, 500m, Intelibill.Domain.Common.BusinessCalendar.StartOf(asOf).AddHours(12));
        DateOnly? dueDate = dueInDays is null ? null : asOf.AddDays(dueInDays.Value);
        if (dueDate is not null)
            typeof(Sale).GetProperty(nameof(Sale.DueDate))!.SetValue(sale, dueDate);
        Arrange(
            fixture,
            [sale],
            [Entry(fixture.shop.Id, customer.Id, CustomerLedgerEntryType.SaleDue, 500m, asOf, sale.Id)],
            [new CustomerInvoiceBalanceDto(sale.Id, sale.InvoiceNumber, sale.SoldAt, 500m, 0m, 0m, 500m, dueDate)]);

        var result = await CreateHandler().HandleAsync(new GetCustomerAccountQuery(fixture.owner.Id, fixture.shop.Id, customer.Id, AsOf: asOf), CancellationToken.None);

        Assert.Equal(dueDate, result.Value.Sales[0].DueDate);
        Assert.Equal(expectedOverdue, result.Value.Sales[0].IsOverdue);
        Assert.Equal(expectedOverdue ? 500m : 0m, result.Value.OverdueAmount);
        Assert.Equal(asOf, result.Value.AsOfBusinessDate);
    }

    [Fact]
    public async Task Paid_off_invoice_past_its_due_date_is_not_overdue()
    {
        var fixture = BuildFixture();
        var asOf = new DateOnly(2026, 11, 6);
        var sale = DueSale(fixture.shop.Id, fixture.customer, 500m, DateTimeOffset.UtcNow.AddDays(-40));
        Arrange(fixture, [sale], [], [new CustomerInvoiceBalanceDto(sale.Id, sale.InvoiceNumber, sale.SoldAt, 500m, 500m, 0m, 0m, asOf.AddDays(-10))]);

        var result = await CreateHandler().HandleAsync(new GetCustomerAccountQuery(fixture.owner.Id, fixture.shop.Id, fixture.customer.Id, AsOf: asOf), CancellationToken.None);

        Assert.False(result.Value.Sales[0].IsOverdue);
    }

    [Fact]
    public async Task Overdue_never_exceeds_what_the_account_owes()
    {
        var fixture = BuildFixture();
        var asOf = new DateOnly(2026, 11, 6);
        var sale = DueSale(fixture.shop.Id, fixture.customer, 500m, DateTimeOffset.UtcNow.AddDays(-40));
        typeof(Sale).GetProperty(nameof(Sale.DueDate))!.SetValue(sale, (DateOnly?)asOf.AddDays(-10));
        // 500 due, overdue on the invoice, but a 400 unallocated payment already pays most of it.
        Arrange(
            fixture,
            [sale],
            [
                Entry(fixture.shop.Id, fixture.customer.Id, CustomerLedgerEntryType.SaleDue, 500m, asOf.AddDays(-40), sale.Id),
                Entry(fixture.shop.Id, fixture.customer.Id, CustomerLedgerEntryType.PaymentReceived, 400m, asOf.AddDays(-5)),
            ],
            [new CustomerInvoiceBalanceDto(sale.Id, sale.InvoiceNumber, sale.SoldAt, 500m, 0m, 0m, 500m, asOf.AddDays(-10))]);

        var result = await CreateHandler().HandleAsync(new GetCustomerAccountQuery(fixture.owner.Id, fixture.shop.Id, fixture.customer.Id, AsOf: asOf), CancellationToken.None);

        Assert.True(result.Value.Sales[0].IsOverdue);
        Assert.Equal(100m, result.Value.OverdueAmount);
    }

    [Fact]
    public async Task Window_that_ends_before_it_starts_is_refused()
    {
        var fixture = BuildFixture();
        Arrange(fixture, [], []);

        var result = await CreateHandler().HandleAsync(
            new GetCustomerAccountQuery(fixture.owner.Id, fixture.shop.Id, fixture.customer.Id, From: new DateOnly(2026, 11, 2), To: new DateOnly(2026, 11, 1)),
            CancellationToken.None);

        Assert.Equal(Errors.Customer.StatementWindowInvalid.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task First_sale_opens_at_zero_and_closes_at_its_due()
    {
        var fixture = BuildFixture();
        var sale = DueSale(fixture.shop.Id, fixture.customer, 500m, DateTimeOffset.UtcNow);
        Arrange(fixture, [sale], [Entry(fixture.shop.Id, fixture.customer.Id, CustomerLedgerEntryType.SaleDue, 500m, new DateOnly(2026, 10, 1), sale.Id)]);

        var result = await CreateHandler().HandleAsync(new GetCustomerAccountQuery(fixture.owner.Id, fixture.shop.Id, fixture.customer.Id), CancellationToken.None);

        Assert.Equal(0m, result.Value.OpeningBalance);
        Assert.Equal(500m, result.Value.ClosingBalance);
        Assert.Equal(500m, result.Value.LedgerEntries[0].RunningBalance);
        Assert.Equal(500m, result.Value.LedgerEntries[0].SignedAmount);
    }

    [Fact]
    public async Task Windowed_statement_opens_with_every_prior_fact_and_reconciles()
    {
        // RF5: opening 500 + sale 100 − payment 200 − return 50 + reversed payment 200 = closing 550.
        var fixture = BuildFixture();
        var shopId = fixture.shop.Id;
        var customerId = fixture.customer.Id;
        var from = new DateOnly(2026, 10, 1);
        var entries = new List<CustomerLedgerEntry>
        {
            Entry(shopId, customerId, CustomerLedgerEntryType.SaleDue, 700m, new DateOnly(2026, 9, 1)),
            Entry(shopId, customerId, CustomerLedgerEntryType.PaymentReceived, 200m, new DateOnly(2026, 9, 15)),
            Entry(shopId, customerId, CustomerLedgerEntryType.SaleDue, 100m, new DateOnly(2026, 10, 2)),
            Entry(shopId, customerId, CustomerLedgerEntryType.PaymentReceived, 200m, new DateOnly(2026, 10, 3)),
            Entry(shopId, customerId, CustomerLedgerEntryType.ReturnCredit, 50m, new DateOnly(2026, 10, 4)),
            Entry(shopId, customerId, CustomerLedgerEntryType.PaymentReversal, 200m, new DateOnly(2026, 10, 5)),
            Entry(shopId, customerId, CustomerLedgerEntryType.SaleDue, 999m, new DateOnly(2026, 11, 1)),
        };
        Arrange(fixture, [], entries);

        var result = await CreateHandler().HandleAsync(
            new GetCustomerAccountQuery(fixture.owner.Id, shopId, customerId, From: from, To: new DateOnly(2026, 10, 31)),
            CancellationToken.None);

        var account = result.Value;
        Assert.Equal(500m, account.OpeningBalance);
        Assert.Equal(550m, account.ClosingBalance);
        Assert.Equal(4, account.LedgerEntries.Count);
        Assert.Equal(account.ClosingBalance, account.OpeningBalance + account.LedgerEntries.Sum(e => e.SignedAmount));
        Assert.Equal(550m, account.LedgerEntries[0].RunningBalance);
        Assert.Equal(1549m, account.OutstandingDue);
    }

    [Fact]
    public async Task Same_day_entries_are_ordered_deterministically()
    {
        var fixture = BuildFixture();
        var day = new DateOnly(2026, 10, 1);
        var a = Entry(fixture.shop.Id, fixture.customer.Id, CustomerLedgerEntryType.SaleDue, 100m, day);
        var b = Entry(fixture.shop.Id, fixture.customer.Id, CustomerLedgerEntryType.PaymentReceived, 40m, day);
        typeof(CustomerLedgerEntry).GetProperty("CreatedAt")!.SetValue(b, a.CreatedAt);
        Arrange(fixture, [], [b, a]);

        var first = await CreateHandler().HandleAsync(new GetCustomerAccountQuery(fixture.owner.Id, fixture.shop.Id, fixture.customer.Id), CancellationToken.None);
        Arrange(fixture, [], [a, b]);
        var second = await CreateHandler().HandleAsync(new GetCustomerAccountQuery(fixture.owner.Id, fixture.shop.Id, fixture.customer.Id), CancellationToken.None);

        Assert.Equal(first.Value.LedgerEntries.Select(e => e.EntryId), second.Value.LedgerEntries.Select(e => e.EntryId));
        Assert.Equal(60m, first.Value.ClosingBalance);
    }
}
