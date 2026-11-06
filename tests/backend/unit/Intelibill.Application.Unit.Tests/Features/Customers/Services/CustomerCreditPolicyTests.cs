using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Customers.Services;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Unit.Tests.Features.Customers.Services;

public class CustomerCreditPolicyTests
{
    private static readonly CustomerCreditState Enforced100 = new(true, CreditPolicyMode.Enforced, 100m);

    [Fact]
    public void NoNewDue_IsAlwaysAllowed_EvenForInactiveNoCreditCustomers()
    {
        var result = CustomerCreditPolicy.Evaluate(new CustomerCreditState(false, CreditPolicyMode.NoCredit, 0m), 50m, 0m, null, ShopRole.Staff);

        Assert.False(result.IsError);
        Assert.Equal(CreditDecisionKind.NoNewDue, result.Value.Kind);
    }

    [Fact]
    public void InactiveCustomer_CannotTakeNewDue_EvenWithOwnerOverride()
    {
        var result = CustomerCreditPolicy.Evaluate(Enforced100 with { IsActive = false }, 0m, 10m, new CreditOverrideRequest("trusted", 0m), ShopRole.Owner);

        Assert.True(result.IsError);
        Assert.Equal(Errors.CustomerCredit.CustomerInactive.Code, result.FirstError.Code);
    }

    [Fact]
    public void NoCredit_RefusesAnyDue_AndCannotBeOverridden()
    {
        var result = CustomerCreditPolicy.Evaluate(new CustomerCreditState(true, CreditPolicyMode.NoCredit, 0m), 0m, 1m, new CreditOverrideRequest("trusted", 0m), ShopRole.Owner);

        Assert.True(result.IsError);
        Assert.Equal(Errors.CustomerCredit.NotExtended.Code, result.FirstError.Code);
    }

    [Theory]
    [InlineData(90, 10)]
    [InlineData(0, 100)]
    [InlineData(-50, 150)]
    public void Enforced_AllowsDueUpToTheLimit_CountingCreditBalanceOnce(decimal exposure, decimal due)
    {
        var result = CustomerCreditPolicy.Evaluate(Enforced100, exposure, due, null, ShopRole.Staff);

        Assert.False(result.IsError);
        Assert.Equal(CreditDecisionKind.WithinLimit, result.Value.Kind);
        Assert.Equal(exposure + due, result.Value.ExposureAfter);
    }

    [Fact]
    public void Enforced_RefusesDueOverTheLimit_WithoutOverride()
    {
        var result = CustomerCreditPolicy.Evaluate(Enforced100, 90m, 10.01m, null, ShopRole.Owner);

        Assert.True(result.IsError);
        Assert.Equal(Errors.CustomerCredit.LimitExceeded.Code, result.FirstError.Code);
    }

    [Fact]
    public void StaffOverride_IsRefused()
    {
        var result = CustomerCreditPolicy.Evaluate(Enforced100, 90m, 50m, new CreditOverrideRequest("regular", 90m), ShopRole.Staff);

        Assert.True(result.IsError);
        Assert.Equal(Errors.CustomerCredit.OverrideNotPermitted.Code, result.FirstError.Code);
    }

    [Fact]
    public void UnknownActorOverride_IsRefused()
    {
        var result = CustomerCreditPolicy.Evaluate(Enforced100, 90m, 50m, new CreditOverrideRequest("regular", 90m), null);

        Assert.Equal(Errors.CustomerCredit.OverrideNotPermitted.Code, result.FirstError.Code);
    }

    [Theory]
    [InlineData(ShopRole.Owner)]
    [InlineData(ShopRole.Manager)]
    public void OwnerOrManagerOverride_IsAllowedAndCarried(ShopRole role)
    {
        var approval = new CreditOverrideRequest("Festival order, pays Friday", 90m);

        var result = CustomerCreditPolicy.Evaluate(Enforced100, 90m, 50m, approval, role);

        Assert.False(result.IsError);
        Assert.Equal(CreditDecisionKind.Overridden, result.Value.Kind);
        Assert.Same(approval, result.Value.Override);
        Assert.Equal(90m, result.Value.ExposureBefore);
    }

    [Fact]
    public void Override_IsStale_WhenTheBalanceGrewAfterApproval()
    {
        var result = CustomerCreditPolicy.Evaluate(Enforced100, 95m, 50m, new CreditOverrideRequest("ok", 90m), ShopRole.Manager);

        Assert.Equal(Errors.CustomerCredit.OverrideStale.Code, result.FirstError.Code);
    }

    [Fact]
    public void Override_NeedsAReason()
    {
        var result = CustomerCreditPolicy.Evaluate(Enforced100, 90m, 50m, new CreditOverrideRequest("   ", 90m), ShopRole.Owner);

        Assert.Equal(Errors.CustomerCredit.OverrideReasonRequired.Code, result.FirstError.Code);
    }

    [Fact]
    public void Override_WithinTheLimit_IsNotRecorded()
    {
        var result = CustomerCreditPolicy.Evaluate(Enforced100, 0m, 10m, new CreditOverrideRequest("not needed", 0m), ShopRole.Owner);

        Assert.Equal(CreditDecisionKind.WithinLimit, result.Value.Kind);
        Assert.Null(result.Value.Override);
    }

    [Fact]
    public void LegacyAdvisory_AllowsDueOverTheLimit_AndFlagsIt()
    {
        var result = CustomerCreditPolicy.Evaluate(new CustomerCreditState(true, CreditPolicyMode.LegacyAdvisory, 100m), 90m, 50m, null, ShopRole.Staff);

        Assert.False(result.IsError);
        Assert.Equal(CreditDecisionKind.Advisory, result.Value.Kind);
        Assert.True(result.Value.ExceedsAdvisoryLimit);
    }

    [Fact]
    public void LegacyAdvisory_WithZeroLimit_IsNotFlaggedAsExceeded()
    {
        var result = CustomerCreditPolicy.Evaluate(new CustomerCreditState(true, CreditPolicyMode.LegacyAdvisory, 0m), 0m, 50m, null, ShopRole.Staff);

        Assert.False(result.Value.ExceedsAdvisoryLimit);
    }

    [Fact]
    public void AvailableCredit_FollowsThePolicy()
    {
        Assert.Equal(10m, CustomerCreditPolicy.AvailableCredit(Enforced100, 90m));
        Assert.Equal(0m, CustomerCreditPolicy.AvailableCredit(Enforced100, 150m));
        Assert.Equal(0m, CustomerCreditPolicy.AvailableCredit(new CustomerCreditState(true, CreditPolicyMode.NoCredit, 500m), 0m));
        Assert.Equal(0m, CustomerCreditPolicy.AvailableCredit(Enforced100 with { IsActive = false }, 0m));
        Assert.Null(CustomerCreditPolicy.AvailableCredit(new CustomerCreditState(true, CreditPolicyMode.LegacyAdvisory, 100m), 0m));
    }
}
