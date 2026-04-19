using FluentValidation.TestHelper;
using Intelibill.Application.Features.Customers.Commands.AddCustomer;
using Intelibill.Application.Features.Customers.Commands.EditCustomer;

namespace Intelibill.Application.Unit.Tests.Features.Customers.Validators;

public class AddCustomerCommandValidatorTests
{
    private readonly AddCustomerCommandValidator _v = new();

    [Fact] public void Validate_WhenNameEmpty_ReturnsError() =>
        _v.TestValidate(new AddCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), "", "+919876543210", null, true))
            .ShouldHaveValidationErrorFor(x => x.Name);

    [Fact] public void Validate_WhenPhoneEmpty_ReturnsError() =>
        _v.TestValidate(new AddCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), "Name", "", null, true))
            .ShouldHaveValidationErrorFor(x => x.PhoneNumber);

    [Fact] public void Validate_WhenPhoneInvalid_ReturnsError() =>
        _v.TestValidate(new AddCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), "Name", "abc", null, true))
            .ShouldHaveValidationErrorFor(x => x.PhoneNumber);

    [Fact] public void Validate_WhenAddressTooLong_ReturnsError() =>
        _v.TestValidate(new AddCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), "Name", "+919876543210", new string('x', 321), true))
            .ShouldHaveValidationErrorFor(x => x.Address);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(new AddCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), "Name", "+919876543210", null, true))
            .ShouldNotHaveAnyValidationErrors();
}

public class EditCustomerCommandValidatorTests
{
    private readonly EditCustomerCommandValidator _v = new();

    [Fact] public void Validate_WhenCustomerIdEmpty_ReturnsError() =>
        _v.TestValidate(new EditCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "Name", "+919876543210", null, true))
            .ShouldHaveValidationErrorFor(x => x.CustomerId);

    [Fact] public void Validate_WhenNameEmpty_ReturnsError() =>
        _v.TestValidate(new EditCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "", "+919876543210", null, true))
            .ShouldHaveValidationErrorFor(x => x.Name);

    [Fact] public void Validate_WhenPhoneInvalid_ReturnsError() =>
        _v.TestValidate(new EditCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Name", "notaphone", null, true))
            .ShouldHaveValidationErrorFor(x => x.PhoneNumber);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(new EditCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Name", "+919876543210", null, true))
            .ShouldNotHaveAnyValidationErrors();
}
