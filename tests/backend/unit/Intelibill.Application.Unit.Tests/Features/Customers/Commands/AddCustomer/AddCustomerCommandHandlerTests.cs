using Intelibill.Application.Features.Customers.Commands.AddCustomer;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Customers.Commands.AddCustomer;

public class AddCustomerCommandHandlerTests
{
    private readonly ICustomerRepository _customerRepository = Substitute.For<ICustomerRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();

    [Fact]
    public async Task HandleAsync_WhenValid_AddsCustomer()
    {
        var shopId = Guid.NewGuid();
        var handler = new AddCustomerCommandHandler(_customerRepository, _unitOfWork, _userRepository);

        var result = await handler.HandleAsync(new AddCustomerCommand(
            shopId,
            "  John Doe  ",
            "  +919876543210  ",
            "  12 Market Road  ",
            true,
            250m), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("John Doe", result.Value.Name);
        Assert.Equal("+919876543210", result.Value.PhoneNumber);
        Assert.Equal("12 Market Road", result.Value.Address);
        Assert.True(result.Value.IsActive);
        Assert.Equal(250m, result.Value.CreditLimit);

        await _customerRepository.Received(1).AddAsync(Arg.Is<Customer>(c =>
            c.ShopId == shopId
            && c.Name == "John Doe"
            && c.PhoneNumber == "+919876543210"
            && c.Address == "12 Market Road"
            && c.IsActive
            && c.CreditLimit == 250m), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static User MemberWithRole(Guid shopId, ShopRole role)
    {
        var user = User.CreateWithEmail($"{role}@test.com".ToLowerInvariant(), "hash", role.ToString(), "User");
        user.AddShopMembership(ShopMembership.Create(shopId, user.Id, role, role == ShopRole.Owner));
        return user;
    }

    [Fact]
    public async Task Add_without_policy_is_labelled_legacy_advisory()
    {
        var shopId = Guid.NewGuid();
        var handler = new AddCustomerCommandHandler(_customerRepository, _unitOfWork, _userRepository);

        var result = await handler.HandleAsync(new AddCustomerCommand(shopId, "A", "+911234567890", null, true, 100m), CancellationToken.None);

        Assert.Equal(CreditPolicyMode.LegacyAdvisory, result.Value.CreditPolicyMode);
    }

    [Fact]
    public async Task Add_with_explicit_no_credit_is_saved()
    {
        var shopId = Guid.NewGuid();
        var owner = MemberWithRole(shopId, ShopRole.Owner);
        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        var handler = new AddCustomerCommandHandler(_customerRepository, _unitOfWork, _userRepository);

        var result = await handler.HandleAsync(
            new AddCustomerCommand(shopId, "A", "+911234567890", null, true, 0m, null, null, CreditPolicyMode.NoCredit, null, owner.Id),
            CancellationToken.None);

        Assert.Equal(CreditPolicyMode.NoCredit, result.Value.CreditPolicyMode);
    }

    [Fact]
    public async Task Staff_added_customers_start_with_no_credit_and_staff_cannot_grant_it()
    {
        var shopId = Guid.NewGuid();
        var staff = MemberWithRole(shopId, ShopRole.Staff);
        _userRepository.GetByIdWithDetailsAsync(staff.Id, Arg.Any<CancellationToken>()).Returns(staff);
        var handler = new AddCustomerCommandHandler(_customerRepository, _unitOfWork, _userRepository);

        var plain = await handler.HandleAsync(new AddCustomerCommand(shopId, "A", "+911234567890", null, true, 0m, ActorUserId: staff.Id), CancellationToken.None);
        var granting = await handler.HandleAsync(new AddCustomerCommand(shopId, "B", "+911234567891", null, true, 500m, ActorUserId: staff.Id), CancellationToken.None);

        Assert.Equal(CreditPolicyMode.NoCredit, plain.Value.CreditPolicyMode);
        Assert.Equal(Errors.CustomerCredit.SettingsChangeNotPermitted.Code, granting.FirstError.Code);
    }

    [Fact]
    public async Task Add_enforced_with_zero_limit_is_refused()
    {
        var handler = new AddCustomerCommandHandler(_customerRepository, _unitOfWork, _userRepository);

        var result = await handler.HandleAsync(
            new AddCustomerCommand(Guid.NewGuid(), "A", "+911234567890", null, true, 0m, null, null, CreditPolicyMode.Enforced),
            CancellationToken.None);

        Assert.Equal("Customer.EnforcedCreditRequiresLimit", result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
