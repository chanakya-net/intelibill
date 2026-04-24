using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Customers.Commands.RecordCustomerPayment;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Customers.Commands.RecordCustomerPayment;

public class RecordCustomerPaymentCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ICustomerRepository _customerRepository = Substitute.For<ICustomerRepository>();
    private readonly ICustomerLedgerEntryRepository _customerLedgerEntryRepository = Substitute.For<ICustomerLedgerEntryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private RecordCustomerPaymentCommandHandler CreateHandler() =>
        new(_userRepository, _shopRepository, _customerRepository, _customerLedgerEntryRepository, _unitOfWork);

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

        var customer = Customer.Create(owner.Id, "Customer A", "+919000000001", null, true);
        return (owner, manager, staff, shop, customer);
    }

    [Fact]
    public async Task HandleAsync_ManagerRole_AddsPaymentEntryAndReturnsDto()
    {
        var fixture = BuildFixture();
        var command = new RecordCustomerPaymentCommand(
            fixture.manager.Id,
            fixture.shop.Id,
            fixture.customer.Id,
            120m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "Received in cash");

        _userRepository.GetByIdWithDetailsAsync(fixture.manager.Id, Arg.Any<CancellationToken>()).Returns(fixture.manager);
        _shopRepository.GetByIdWithMembersAsync(fixture.shop.Id, Arg.Any<CancellationToken>()).Returns(fixture.shop);
        _customerRepository.GetByOwnerAndIdAsync(fixture.owner.Id, fixture.customer.Id, Arg.Any<CancellationToken>())
            .Returns(fixture.customer);
        _customerLedgerEntryRepository.GetCustomerBalanceAsync(fixture.shop.Id, fixture.customer.Id, Arg.Any<CancellationToken>())
            .Returns(80m);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(CustomerLedgerEntryType.PaymentReceived, result.Value.EntryType);
        Assert.Equal(80m, result.Value.RunningBalance);
        await _customerLedgerEntryRepository.Received(1).AddAsync(
            Arg.Is<CustomerLedgerEntry>(entry =>
                entry.ShopId == fixture.shop.Id &&
                entry.CustomerId == fixture.customer.Id &&
                entry.EntryType == CustomerLedgerEntryType.PaymentReceived &&
                entry.Amount == 120m),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_StaffRole_ReturnsForbidden()
    {
        var fixture = BuildFixture();
        var command = new RecordCustomerPaymentCommand(
            fixture.staff.Id,
            fixture.shop.Id,
            fixture.customer.Id,
            120m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            null);

        _userRepository.GetByIdWithDetailsAsync(fixture.staff.Id, Arg.Any<CancellationToken>()).Returns(fixture.staff);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Customer.UserIsNotOwnerOrManager.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_ActorNotFound_ReturnsUserNotFound()
    {
        var fixture = BuildFixture();
        var command = new RecordCustomerPaymentCommand(
            Guid.NewGuid(),
            fixture.shop.Id,
            fixture.customer.Id,
            100m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            null);

        _userRepository.GetByIdWithDetailsAsync(command.ActorUserId, Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.UserNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_MembershipMissing_ReturnsMembershipNotFound()
    {
        var fixture = BuildFixture();
        var outsider = User.CreateWithEmail("outsider@test.com", "hash", "Out", "Sider");
        var command = new RecordCustomerPaymentCommand(
            outsider.Id,
            fixture.shop.Id,
            fixture.customer.Id,
            100m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            null);

        _userRepository.GetByIdWithDetailsAsync(outsider.Id, Arg.Any<CancellationToken>())
            .Returns(outsider);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_CustomerNotFound_ReturnsCustomerNotFound()
    {
        var fixture = BuildFixture();
        var command = new RecordCustomerPaymentCommand(
            fixture.manager.Id,
            fixture.shop.Id,
            Guid.NewGuid(),
            120m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            null);

        _userRepository.GetByIdWithDetailsAsync(fixture.manager.Id, Arg.Any<CancellationToken>()).Returns(fixture.manager);
        _shopRepository.GetByIdWithMembersAsync(fixture.shop.Id, Arg.Any<CancellationToken>()).Returns(fixture.shop);
        _customerRepository.GetByOwnerAndIdAsync(fixture.owner.Id, command.CustomerId, Arg.Any<CancellationToken>())
            .Returns((Customer?)null);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Customer.CustomerNotFound.Code, result.FirstError.Code);
    }
}
