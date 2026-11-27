using Intelibill.Application.Common.Security;
using FluentValidation.TestHelper;
using Intelibill.Application.Features.Customers.Commands.AddCustomer;
using Intelibill.Application.Features.Customers.Commands.EditCustomer;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Unit.Tests.Features.Customers.Validators;

public class AddCustomerCommandValidatorTests
{
    private readonly AddCustomerCommandValidator _v = new();
    private readonly SessionClaims _session = new(Guid.NewGuid(), Guid.NewGuid(), 0, 0, Guid.NewGuid(), "Owner");

    [Fact] public void Validate_WhenNameEmpty_ReturnsError() =>
        _v.TestValidate(new AddCustomerCommand(_session, "", "+919876543210", null, true, 0m, CreditPolicyMode.NoCredit))
            .ShouldHaveValidationErrorFor(x => x.Name);

    [Fact] public void Validate_WhenPhoneEmpty_ReturnsError() =>
        _v.TestValidate(new AddCustomerCommand(_session, "Name", "", null, true, 0m, CreditPolicyMode.NoCredit))
            .ShouldHaveValidationErrorFor(x => x.PhoneNumber);

    [Fact] public void Validate_WhenPhoneInvalid_ReturnsError() =>
        _v.TestValidate(new AddCustomerCommand(_session, "Name", "abc", null, true, 0m, CreditPolicyMode.NoCredit))
            .ShouldHaveValidationErrorFor(x => x.PhoneNumber);

    [Fact] public void Validate_WhenAddressTooLong_ReturnsError() =>
        _v.TestValidate(new AddCustomerCommand(_session, "Name", "+919876543210", new string('x', 321), true, 0m, CreditPolicyMode.NoCredit))
            .ShouldHaveValidationErrorFor(x => x.Address);

    [Fact] public void Validate_WhenCreditLimitNegative_ReturnsError() =>
        _v.TestValidate(new AddCustomerCommand(_session, "Name", "+919876543210", null, true, -1m, CreditPolicyMode.NoCredit))
            .ShouldHaveValidationErrorFor(x => x.CreditLimit);

    [Fact] public void Validate_WhenCreditLimitAboveMax_ReturnsError() =>
        _v.TestValidate(new AddCustomerCommand(_session, "Name", "+919876543210", null, true, 100000000m, CreditPolicyMode.NoCredit))
            .ShouldHaveValidationErrorFor(x => x.CreditLimit);

    [Fact] public void Validate_WhenCreditLimitHasMoreThanTwoDecimalPlaces_ReturnsError() =>
        _v.TestValidate(new AddCustomerCommand(_session, "Name", "+919876543210", null, true, 10.999m, CreditPolicyMode.NoCredit))
            .ShouldHaveValidationErrorFor(x => x.CreditLimit);

    [Fact] public void Validate_WhenCreditLimitIsZero_NoErrors() =>
        _v.TestValidate(new AddCustomerCommand(_session, "Name", "+919876543210", null, true, 0m, CreditPolicyMode.NoCredit))
            .ShouldNotHaveAnyValidationErrors();

    [Fact] public void Validate_WhenCreditLimitIsPositive_NoErrors() =>
        _v.TestValidate(new AddCustomerCommand(_session, "Name", "+919876543210", null, true, 99999999.99m, CreditPolicyMode.NoCredit))
            .ShouldNotHaveAnyValidationErrors();

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(new AddCustomerCommand(_session, "Name", "+919876543210", null, true, 0m, CreditPolicyMode.NoCredit))
            .ShouldNotHaveAnyValidationErrors();

    [Fact] public void Validate_WhenPolicyMissing_ReturnsError() =>
        _v.TestValidate(new AddCustomerCommand(_session, "Name", "+919876543210", null, true, 0m, default))
            .ShouldHaveValidationErrorFor(x => x.CreditPolicyMode)
            .WithErrorCode("Customer.CreditPolicyModeInvalid");
}

public class EditCustomerCommandValidatorTests
{
    private readonly EditCustomerCommandValidator _v = new();

    [Fact] public void Validate_WhenCustomerIdEmpty_ReturnsError() =>
        _v.TestValidate(new EditCustomerCommand(Guid.NewGuid(), Guid.Empty, "Name", "+919876543210", null, true, 0m, CreditPolicyMode.NoCredit, Session: null))
            .ShouldHaveValidationErrorFor(x => x.CustomerId);

    [Fact] public void Validate_WhenNameEmpty_ReturnsError() =>
        _v.TestValidate(new EditCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), "", "+919876543210", null, true, 0m, CreditPolicyMode.NoCredit, Session: null))
            .ShouldHaveValidationErrorFor(x => x.Name);

    [Fact] public void Validate_WhenPhoneInvalid_ReturnsError() =>
        _v.TestValidate(new EditCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), "Name", "notaphone", null, true, 0m, CreditPolicyMode.NoCredit, Session: null))
            .ShouldHaveValidationErrorFor(x => x.PhoneNumber);

    [Fact] public void Validate_WhenCreditLimitNegative_ReturnsError() =>
        _v.TestValidate(new EditCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), "Name", "+919876543210", null, true, -0.01m, CreditPolicyMode.NoCredit, Session: null))
            .ShouldHaveValidationErrorFor(x => x.CreditLimit);

    [Fact] public void Validate_WhenCreditLimitAboveMax_ReturnsError() =>
        _v.TestValidate(new EditCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), "Name", "+919876543210", null, true, 99999999.999m, CreditPolicyMode.NoCredit, Session: null))
            .ShouldHaveValidationErrorFor(x => x.CreditLimit);

    [Fact] public void Validate_WhenCreditLimitHasMoreThanTwoDecimalPlaces_ReturnsError() =>
        _v.TestValidate(new EditCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), "Name", "+919876543210", null, true, 10.999m, CreditPolicyMode.NoCredit, Session: null))
            .ShouldHaveValidationErrorFor(x => x.CreditLimit);

    [Fact] public void Validate_WhenCreditLimitIsZero_NoErrors() =>
        _v.TestValidate(new EditCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), "Name", "+919876543210", null, true, 0m, CreditPolicyMode.NoCredit, Session: null))
            .ShouldNotHaveAnyValidationErrors();

    [Fact] public void Validate_WhenCreditLimitAtMax_NoErrors() =>
        _v.TestValidate(new EditCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), "Name", "+919876543210", null, true, 99999999.99m, CreditPolicyMode.NoCredit, Session: null))
            .ShouldNotHaveAnyValidationErrors();

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(new EditCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), "Name", "+919876543210", null, true, 0m, CreditPolicyMode.NoCredit, Session: null))
            .ShouldNotHaveAnyValidationErrors();

    [Fact] public void Validate_WhenStateContradictsTheGstin_ReturnsError() =>
        _v.TestValidate(new EditCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), "Name", "+919876543210", null, true, 0m, CreditPolicyMode.NoCredit, Session: null, Gstin: "33AABCT1332L1ZL", StateCode: "29"))
            .ShouldHaveValidationErrorFor(nameof(EditCustomerCommand.StateCode))
            .WithErrorCode("Customer.GstinStateMismatch");

    [Fact] public void Validate_WhenPolicyMissing_ReturnsError() =>
        _v.TestValidate(new EditCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), "Name", "+919876543210", null, true, 0m, default, Session: null))
            .ShouldHaveValidationErrorFor(x => x.CreditPolicyMode)
            .WithErrorCode("Customer.CreditPolicyModeInvalid");
}
