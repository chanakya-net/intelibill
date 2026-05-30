using FluentValidation.TestHelper;
using Intelibill.Application.Features.Suppliers.Commands.AddSupplier;
using Intelibill.Application.Features.Suppliers.Commands.EditSupplier;
using Intelibill.Application.Features.SupplierLedger.Commands.MakeSupplierPayment;

namespace Intelibill.Application.Unit.Tests.Features.Suppliers.Validators;

public class AddSupplierCommandValidatorTests
{
    private readonly AddSupplierCommandValidator _v = new();

    private static AddSupplierCommand Valid() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Supplier Name", null, null, "123 Main St", "Mumbai", "Maharashtra", "400001", true, false);

    [Fact] public void Validate_WhenNameEmpty_ReturnsError() =>
        _v.TestValidate(Valid() with { Name = "" }).ShouldHaveValidationErrorFor(x => x.Name);

    [Fact] public void Validate_WhenAddressEmpty_ReturnsError() =>
        _v.TestValidate(Valid() with { Address = "" }).ShouldHaveValidationErrorFor(x => x.Address);

    [Fact] public void Validate_WhenCityEmpty_ReturnsError() =>
        _v.TestValidate(Valid() with { City = "" }).ShouldHaveValidationErrorFor(x => x.City);

    [Fact] public void Validate_WhenContactPhoneInvalid_ReturnsError() =>
        _v.TestValidate(Valid() with { ContactPersonPhone = "notaphone" }).ShouldHaveValidationErrorFor(x => x.ContactPersonPhone);

    [Fact] public void Validate_WhenContactPhoneNull_NoPhoneError() =>
        _v.TestValidate(Valid() with { ContactPersonPhone = null }).ShouldNotHaveValidationErrorFor(x => x.ContactPersonPhone);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
}

public class EditSupplierCommandValidatorTests
{
    private readonly EditSupplierCommandValidator _v = new();

    private static EditSupplierCommand Valid() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Supplier Name", null, null, "123 Main St", "Mumbai", "Maharashtra", "400001", true, false);

    [Fact] public void Validate_WhenNameEmpty_ReturnsError() =>
        _v.TestValidate(Valid() with { Name = "" }).ShouldHaveValidationErrorFor(x => x.Name);

    [Fact] public void Validate_WhenStateEmpty_ReturnsError() =>
        _v.TestValidate(Valid() with { State = "" }).ShouldHaveValidationErrorFor(x => x.State);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
}

public class MakeSupplierPaymentCommandValidatorTests
{
    private readonly MakeSupplierPaymentCommandValidator _v = new();

    private static MakeSupplierPaymentCommand Valid() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 500m, new DateOnly(2026, 4, 1), null);

    [Fact] public void Validate_WhenAmountZero_ReturnsError() =>
        _v.TestValidate(Valid() with { Amount = 0m }).ShouldHaveValidationErrorFor(x => x.Amount);

    [Fact] public void Validate_WhenAmountNegative_ReturnsError() =>
        _v.TestValidate(Valid() with { Amount = -1m }).ShouldHaveValidationErrorFor(x => x.Amount);

    [Fact] public void Validate_WhenPaymentDateIsMinValue_ReturnsError() =>
        _v.TestValidate(Valid() with { PaymentDate = DateOnly.MinValue }).ShouldHaveValidationErrorFor(x => x.PaymentDate);

    [Fact] public void Validate_WhenNotesTooLong_ReturnsError() =>
        _v.TestValidate(Valid() with { Notes = new string('x', 501) }).ShouldHaveValidationErrorFor(x => x.Notes);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
}
