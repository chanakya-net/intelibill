using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Customers.Queries.GetCustomerAccount;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Customers.Queries.GetCustomerAccount;

public class GetCustomerAccountQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ICustomerRepository _customerRepository = Substitute.For<ICustomerRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly ICustomerLedgerEntryRepository _customerLedgerEntryRepository = Substitute.For<ICustomerLedgerEntryRepository>();

    private GetCustomerAccountQueryHandler CreateHandler() =>
        new(_userRepository, _customerRepository, _saleRepository, _customerLedgerEntryRepository);

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
            []);

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

        _userRepository.GetByIdWithDetailsAsync(fixture.manager.Id, Arg.Any<CancellationToken>()).Returns(fixture.manager);
        _customerRepository.GetByShopAndIdAsync(fixture.shop.Id, fixture.customer.Id, Arg.Any<CancellationToken>()).Returns(fixture.customer);
        _saleRepository.GetByCustomerAsync(fixture.shop.Id, fixture.customer.Id, Arg.Any<CancellationToken>()).Returns([sale]);
        _customerLedgerEntryRepository.GetByCustomerAsync(fixture.shop.Id, fixture.customer.Id, Arg.Any<CancellationToken>())
            .Returns([dueEntry, paymentEntry]);
        _customerLedgerEntryRepository.GetCustomerBalanceAsync(fixture.shop.Id, fixture.customer.Id, Arg.Any<CancellationToken>())
            .Returns(15m);

        var result = await CreateHandler().HandleAsync(query, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(15m, result.Value.OutstandingDue);
        Assert.Single(result.Value.Sales);
        Assert.Equal(paymentEntry.Id, result.Value.LedgerEntries[0].EntryId);
        Assert.Single(result.Value.PaymentHistory);
        Assert.Equal(paymentEntry.Id, result.Value.PaymentHistory[0].EntryId);
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
}
