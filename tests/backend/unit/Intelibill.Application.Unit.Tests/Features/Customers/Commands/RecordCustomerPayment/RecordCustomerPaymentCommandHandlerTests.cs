using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Features.Customers.DTOs;
using Intelibill.Application.Features.Customers.Commands.RecordCustomerPayment;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Customers.Commands.RecordCustomerPayment;

public class RecordCustomerPaymentCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ICustomerRepository _customerRepository = Substitute.For<ICustomerRepository>();
    private readonly ICustomerLedgerEntryRepository _customerLedgerEntryRepository = Substitute.For<ICustomerLedgerEntryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IFinancialOperationExecutor _executor = Substitute.For<IFinancialOperationExecutor>();

    private RecordCustomerPaymentCommandHandler CreateHandler() =>
        new(_userRepository, _customerRepository, _customerLedgerEntryRepository, _unitOfWork, _executor,
            NullLogger<RecordCustomerPaymentCommandHandler>.Instance);

    private void ExecutorRunsAction() =>
        _executor.ExecuteAsync(
                Arg.Any<FinancialOperationScope>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task<ErrorOr<CustomerLedgerEntryDto>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<ErrorOr<CustomerLedgerEntryDto>>>>()(CancellationToken.None));

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

        var customer = Customer.Create(shop.Id, "Customer A", "+919000000001", null, true);
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
        _customerRepository.GetByShopAndIdAsync(fixture.shop.Id, fixture.customer.Id, Arg.Any<CancellationToken>())
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
            fixture.staff.Id, fixture.shop.Id, fixture.customer.Id,
            120m, DateOnly.FromDateTime(DateTime.UtcNow), null);

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
            Guid.NewGuid(), fixture.shop.Id, fixture.customer.Id,
            100m, DateOnly.FromDateTime(DateTime.UtcNow), null);

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
            outsider.Id, fixture.shop.Id, fixture.customer.Id,
            100m, DateOnly.FromDateTime(DateTime.UtcNow), null);

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
            fixture.manager.Id, fixture.shop.Id, Guid.NewGuid(),
            120m, DateOnly.FromDateTime(DateTime.UtcNow), null);

        _userRepository.GetByIdWithDetailsAsync(fixture.manager.Id, Arg.Any<CancellationToken>()).Returns(fixture.manager);
        _customerRepository.GetByShopAndIdAsync(fixture.shop.Id, command.CustomerId, Arg.Any<CancellationToken>())
            .Returns((Customer?)null);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Customer.CustomerNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WithOperationKey_RunsPostingThroughExecutorInShopScope()
    {
        var fixture = BuildFixture();
        var command = new RecordCustomerPaymentCommand(
            fixture.manager.Id, fixture.shop.Id, fixture.customer.Id,
            500m, new DateOnly(2026, 11, 1), "cash", "  pay-1  ");

        _userRepository.GetByIdWithDetailsAsync(fixture.manager.Id, Arg.Any<CancellationToken>()).Returns(fixture.manager);
        _customerRepository.GetByShopAndIdAsync(fixture.shop.Id, fixture.customer.Id, Arg.Any<CancellationToken>())
            .Returns(fixture.customer);
        ExecutorRunsAction();

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        await _executor.Received(1).ExecuteAsync(
            new FinancialOperationScope(fixture.shop.Id, FinancialOperationType.CustomerPayment, "pay-1"),
            fixture.manager.Id,
            fixture.customer.Id,
            Arg.Is<string>(hash => hash.Length == 64),
            Arg.Any<Func<CancellationToken, Task<ErrorOr<CustomerLedgerEntryDto>>>>(),
            Arg.Any<CancellationToken>());
        await _customerLedgerEntryRepository.Received(1).AddAsync(Arg.Any<CustomerLedgerEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_StaffWithOperationKey_NeverReachesReceipt()
    {
        var fixture = BuildFixture();
        var command = new RecordCustomerPaymentCommand(
            fixture.staff.Id, fixture.shop.Id, fixture.customer.Id,
            500m, new DateOnly(2026, 11, 1), null, "pay-1");

        _userRepository.GetByIdWithDetailsAsync(fixture.staff.Id, Arg.Any<CancellationToken>()).Returns(fixture.staff);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.Equal(Errors.Customer.UserIsNotOwnerOrManager.Code, result.FirstError.Code);
        Assert.Empty(_executor.ReceivedCalls());
    }

    [Fact]
    public async Task HandleAsync_WithoutOperationKey_PostsWithoutReceipt()
    {
        var fixture = BuildFixture();
        var command = new RecordCustomerPaymentCommand(
            fixture.manager.Id, fixture.shop.Id, fixture.customer.Id,
            500m, new DateOnly(2026, 11, 1), null);

        _userRepository.GetByIdWithDetailsAsync(fixture.manager.Id, Arg.Any<CancellationToken>()).Returns(fixture.manager);
        _customerRepository.GetByShopAndIdAsync(fixture.shop.Id, fixture.customer.Id, Arg.Any<CancellationToken>())
            .Returns(fixture.customer);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(_executor.ReceivedCalls());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
