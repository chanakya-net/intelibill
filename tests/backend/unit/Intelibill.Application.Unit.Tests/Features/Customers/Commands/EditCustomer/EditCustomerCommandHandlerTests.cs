using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Customers.Commands.EditCustomer;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Customers.Commands.EditCustomer;

public class EditCustomerCommandHandlerTests
{
    private readonly ICustomerRepository _customerRepository = Substitute.For<ICustomerRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly EditCustomerCommandHandler _handler;

    public EditCustomerCommandHandlerTests()
    {
        _handler = new EditCustomerCommandHandler(_customerRepository, _unitOfWork, _userRepository);
    }

    [Fact]
    public async Task HandleAsync_WhenCustomerNotFoundInShop_ReturnsNotFoundError()
    {
        var shopId = Guid.NewGuid();
        _customerRepository.GetByShopAndIdAsync(shopId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Customer?)null);

        var command = new EditCustomerCommand(shopId, Guid.NewGuid(), "New Name", "+919876543210", null, true, 0m);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Customer.CustomerNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenValid_UpdatesCustomerAndReturnsDto()
    {
        var shopId = Guid.NewGuid();
        var customer = Customer.Create(shopId, "Old Name", "+911234567890", null);
        _customerRepository.GetByShopAndIdAsync(shopId, customer.Id, Arg.Any<CancellationToken>())
            .Returns(customer);

        var command = new EditCustomerCommand(shopId, customer.Id, "New Name", "+919999999999", "12 MG Road", false, 750m);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("New Name", result.Value.Name);
        Assert.Equal("+919999999999", result.Value.PhoneNumber);
        Assert.Equal("12 MG Road", result.Value.Address);
        Assert.False(result.Value.IsActive);
        Assert.Equal(750m, result.Value.CreditLimit);
        _customerRepository.Received(1).Update(customer);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenGstinOmitted_KeepsTheRecordedRegistration()
    {
        var shopId = Guid.NewGuid();
        var customer = Customer.Create(shopId, "Buyer", "+911234567890", "4 Market St", gstin: "33AABCT1332L1ZL");
        _customerRepository.GetByShopAndIdAsync(shopId, customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var result = await _handler.HandleAsync(
            new EditCustomerCommand(shopId, customer.Id, "Buyer Renamed", "+911234567890", "4 Market St", true, 0m),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("33AABCT1332L1ZL", customer.Gstin);
        Assert.Equal("33", customer.StateCode);
        Assert.Equal("33AABCT1332L1ZL", result.Value.Gstin);
    }

    [Fact]
    public async Task HandleAsync_WhenGstinEmpty_ClearsIt()
    {
        var shopId = Guid.NewGuid();
        var customer = Customer.Create(shopId, "Buyer", "+911234567890", "4 Market St", gstin: "33AABCT1332L1ZL");
        _customerRepository.GetByShopAndIdAsync(shopId, customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var result = await _handler.HandleAsync(
            new EditCustomerCommand(shopId, customer.Id, "Buyer", "+911234567890", "4 Market St", true, 0m, Gstin: "", StateCode: "29"),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Null(customer.Gstin);
        Assert.Equal("29", customer.StateCode);
    }

    [Fact]
    public async Task HandleAsync_WhenStateContradictsTheKeptGstin_IsRefused()
    {
        var shopId = Guid.NewGuid();
        var customer = Customer.Create(shopId, "Buyer", "+911234567890", "4 Market St", gstin: "33AABCT1332L1ZL");
        _customerRepository.GetByShopAndIdAsync(shopId, customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var result = await _handler.HandleAsync(
            new EditCustomerCommand(shopId, customer.Id, "Buyer", "+911234567890", "4 Market St", true, 0m, StateCode: "29"),
            CancellationToken.None);

        Assert.Equal(Errors.Customer.GstinStateMismatch.Code, result.FirstError.Code);
        Assert.Equal("33", customer.StateCode);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static User MemberWithRole(Guid shopId, ShopRole role)
    {
        var user = User.CreateWithEmail($"{role}@test.com".ToLowerInvariant(), "hash", role.ToString(), "User");
        user.AddShopMembership(ShopMembership.Create(shopId, user.Id, role, role == ShopRole.Owner));
        return user;
    }

    [Fact]
    public async Task Edit_without_policy_keeps_the_stored_policy_and_terms()
    {
        var shopId = Guid.NewGuid();
        var customer = Customer.Create(shopId, "A", "+911234567890", null, true, 500m);
        Assert.False(customer.SetCreditPolicy(CreditPolicyMode.Enforced, 30).IsError);
        _customerRepository.GetByShopAndIdAsync(shopId, customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var result = await _handler.HandleAsync(new EditCustomerCommand(shopId, customer.Id, "B", "+911234567890", null, true, 500m), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(CreditPolicyMode.Enforced, result.Value.CreditPolicyMode);
        Assert.Equal(30, result.Value.CreditTermsDays);
    }

    [Fact]
    public async Task Edit_cannot_turn_an_enforced_limit_into_zero()
    {
        var shopId = Guid.NewGuid();
        var customer = Customer.Create(shopId, "A", "+911234567890", null, true, 500m);
        Assert.False(customer.SetCreditPolicy(CreditPolicyMode.Enforced, null).IsError);
        _customerRepository.GetByShopAndIdAsync(shopId, customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var result = await _handler.HandleAsync(new EditCustomerCommand(shopId, customer.Id, "A", "+911234567890", null, true, 0m), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Customer.EnforcedCreditRequiresLimit", result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Staff_cannot_change_credit_settings_but_may_edit_details()
    {
        var shopId = Guid.NewGuid();
        var staff = MemberWithRole(shopId, ShopRole.Staff);
        _userRepository.GetByIdWithDetailsAsync(staff.Id, Arg.Any<CancellationToken>()).Returns(staff);
        var customer = Customer.Create(shopId, "A", "+911234567890", null, true, 500m);
        _customerRepository.GetByShopAndIdAsync(shopId, customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var raise = await _handler.HandleAsync(
            new EditCustomerCommand(shopId, customer.Id, "A", "+911234567890", null, true, 5000m, ActorUserId: staff.Id), CancellationToken.None);
        var rename = await _handler.HandleAsync(
            new EditCustomerCommand(shopId, customer.Id, "Renamed", "+911234567890", null, true, 500m, ActorUserId: staff.Id), CancellationToken.None);

        Assert.Equal(Errors.CustomerCredit.SettingsChangeNotPermitted.Code, raise.FirstError.Code);
        Assert.False(rename.IsError);
    }

    [Fact]
    public async Task Staff_cannot_reactivate_a_customer_who_may_take_credit_but_may_deactivate()
    {
        var shopId = Guid.NewGuid();
        var staff = MemberWithRole(shopId, ShopRole.Staff);
        _userRepository.GetByIdWithDetailsAsync(staff.Id, Arg.Any<CancellationToken>()).Returns(staff);
        var inactive = Customer.Create(shopId, "A", "+911234567890", null, isActive: false, creditLimit: 500m);
        var active = Customer.Create(shopId, "B", "+911234567891", null, isActive: true, creditLimit: 500m);
        _customerRepository.GetByShopAndIdAsync(shopId, inactive.Id, Arg.Any<CancellationToken>()).Returns(inactive);
        _customerRepository.GetByShopAndIdAsync(shopId, active.Id, Arg.Any<CancellationToken>()).Returns(active);

        var reactivate = await _handler.HandleAsync(
            new EditCustomerCommand(shopId, inactive.Id, "A", "+911234567890", null, true, 500m, ActorUserId: staff.Id), CancellationToken.None);
        var deactivate = await _handler.HandleAsync(
            new EditCustomerCommand(shopId, active.Id, "B", "+911234567891", null, false, 500m, ActorUserId: staff.Id), CancellationToken.None);

        Assert.Equal(Errors.CustomerCredit.SettingsChangeNotPermitted.Code, reactivate.FirstError.Code);
        Assert.False(deactivate.IsError);
    }

    [Fact]
    public async Task Manager_can_choose_an_enforced_policy_with_terms()
    {
        var shopId = Guid.NewGuid();
        var manager = MemberWithRole(shopId, ShopRole.Manager);
        _userRepository.GetByIdWithDetailsAsync(manager.Id, Arg.Any<CancellationToken>()).Returns(manager);
        var customer = Customer.Create(shopId, "A", "+911234567890", null, true, 0m);
        _customerRepository.GetByShopAndIdAsync(shopId, customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var result = await _handler.HandleAsync(
            new EditCustomerCommand(shopId, customer.Id, "A", "+911234567890", null, true, 2000m, null, null, CreditPolicyMode.Enforced, 15, manager.Id),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(CreditPolicyMode.Enforced, customer.CreditPolicyMode);
        Assert.Equal(15, customer.CreditTermsDays);
        Assert.Equal(2000m, customer.CreditLimit);
    }
}
