using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Security;
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

    private readonly ILiveSessionAuthority _authority = Substitute.For<ILiveSessionAuthority>();
    private readonly IShopLicenceFence _fence = Substitute.For<IShopLicenceFence>();
    private static CancellationToken Ct => CancellationToken.None;

    public AddCustomerCommandHandlerTests()
    {
        _authority.IsValidAsync(Arg.Any<SessionClaims>(), Ct).Returns(true);
        _fence.EnterAsync(Arg.Any<Guid>(), LicenceAction.OperationalCapacity, Ct).Returns(
            call => new ShopLicenceLease(call.Arg<Guid>(), null, null, null, new(LicenceOutcome.Allowed, true)));
    }

    [Fact]
    public async Task HandleAsync_WhenValid_AddsCustomer()
    {
        var shopId = Guid.NewGuid();
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new AddCustomerCommand(
            AddMember(shopId),
            "  John Doe  ",
            "  +919876543210  ",
            "  12 Market Road  ",
            true,
            250m,
            CreditPolicyMode.Enforced), CancellationToken.None);

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
        await _unitOfWork.Received(1).SaveChangesAndCommitAsync(Arg.Any<CancellationToken>());
    }

    private SessionClaims AddMember(Guid shopId, ShopRole role = ShopRole.Owner)
    {
        var user = User.CreateWithEmail($"{role}@test.com".ToLowerInvariant(), "hash", role.ToString(), "User");
        user.AddShopMembership(ShopMembership.Create(shopId, user.Id, role, role == ShopRole.Owner));
        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        return new(user.Id, Guid.NewGuid(), 0, 0, shopId, role.ToString());
    }

    [Fact]
    public async Task Add_with_explicit_no_credit_is_saved()
    {
        var shopId = Guid.NewGuid();
        var owner = AddMember(shopId);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(
            new AddCustomerCommand(owner, "A", "+911234567890", null, true, 0m, CreditPolicyMode.NoCredit),
            CancellationToken.None);

        Assert.Equal(CreditPolicyMode.NoCredit, result.Value.CreditPolicyMode);
    }

    [Fact]
    public async Task Staff_added_customers_start_with_no_credit_and_staff_cannot_grant_it()
    {
        var shopId = Guid.NewGuid();
        var staff = AddMember(shopId, ShopRole.Staff);
        var handler = CreateHandler();

        var plain = await handler.HandleAsync(new AddCustomerCommand(staff, "A", "+911234567890", null, true, 0m, CreditPolicyMode.NoCredit), CancellationToken.None);
        var granting = await handler.HandleAsync(new AddCustomerCommand(staff, "B", "+911234567891", null, true, 500m, CreditPolicyMode.NoCredit), CancellationToken.None);
        var enforcing = await handler.HandleAsync(new AddCustomerCommand(staff, "C", "+911234567892", null, true, 0m, CreditPolicyMode.Enforced), CancellationToken.None);

        Assert.Equal(CreditPolicyMode.NoCredit, plain.Value.CreditPolicyMode);
        Assert.Equal(Errors.CustomerCredit.SettingsChangeNotPermitted.Code, granting.FirstError.Code);
        Assert.Equal(Errors.CustomerCredit.SettingsChangeNotPermitted.Code, enforcing.FirstError.Code);
    }

    [Fact]
    public async Task Add_with_terms_saves_them_with_the_policy()
    {
        var result = await CreateHandler().HandleAsync(
            new AddCustomerCommand(AddMember(Guid.NewGuid()), "A", "+911234567890", null, true, 100m, CreditPolicyMode.Enforced, CreditTermsDays: 30),
            CancellationToken.None);

        Assert.Equal(CreditPolicyMode.Enforced, result.Value.CreditPolicyMode);
        Assert.Equal(30, result.Value.CreditTermsDays);
    }

    [Fact]
    public async Task Add_enforced_with_zero_limit_is_refused()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(
            new AddCustomerCommand(AddMember(Guid.NewGuid()), "A", "+911234567890", null, true, 0m, CreditPolicyMode.Enforced),
            CancellationToken.None);

        Assert.Equal("Customer.EnforcedCreditRequiresLimit", result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAndCommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("expired", "Licence.Expired")]
    [InlineData("busy", "Licence.Busy")]
    [InlineData("revoked", "Auth.SessionInvalidated")]
    public async Task Creation_denial_does_not_insert_or_commit(string refusal, string expected)
    {
        var shop = Guid.NewGuid();
        var session = AddMember(shop);
        if (refusal == "expired")
            _fence.EnterAsync(shop, LicenceAction.OperationalCapacity, Ct).Returns(
                new ShopLicenceLease(shop, null, null, null, new(LicenceOutcome.Blocked, true, Errors.Licence.Expired)));
        if (refusal == "busy")
            _fence.EnterAsync(shop, LicenceAction.OperationalCapacity, Ct).Returns((ErrorOr<ShopLicenceLease>)Errors.Licence.Busy);
        if (refusal == "revoked")
            _authority.IsValidAsync(session, Ct).Returns(false);

        var result = await CreateHandler().HandleAsync(new(session, "A", "+911234567890", null, true, 0m, CreditPolicyMode.NoCredit), Ct);

        Assert.Equal(expected, result.FirstError.Code);
        await _customerRepository.DidNotReceive().AddAsync(Arg.Any<Customer>(), Ct);
        await _unitOfWork.DidNotReceive().SaveChangesAndCommitAsync(Ct);
    }

    private AddCustomerCommandHandler CreateHandler() =>
        new(_customerRepository, new(_unitOfWork, _authority, _fence), _userRepository);
}
