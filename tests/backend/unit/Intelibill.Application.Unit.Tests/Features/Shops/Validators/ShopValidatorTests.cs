using FluentValidation.TestHelper;
using Intelibill.Application.Features.Shops.Commands.AddShopBankAccount;
using Intelibill.Application.Features.Shops.Commands.CreateShop;
using Intelibill.Application.Features.Shops.Commands.UpdateShop;

namespace Intelibill.Application.Unit.Tests.Features.Shops.Validators;

public class CreateShopCommandValidatorTests
{
    private readonly CreateShopCommandValidator _v = new();

    private static CreateShopCommand Valid() =>
        new(Guid.NewGuid(), "Test Shop", "42 MG Road", "Bengaluru", "Karnataka", "560001", null, null, null);

    [Fact] public void Validate_WhenNameEmpty_ReturnsError() =>
        _v.TestValidate(Valid() with { Name = "" }).ShouldHaveValidationErrorFor(x => x.Name);

    [Fact] public void Validate_WhenAddressEmpty_ReturnsError() =>
        _v.TestValidate(Valid() with { Address = "" }).ShouldHaveValidationErrorFor(x => x.Address);

    [Fact] public void Validate_WhenCityEmpty_ReturnsError() =>
        _v.TestValidate(Valid() with { City = "" }).ShouldHaveValidationErrorFor(x => x.City);

    [Fact] public void Validate_WhenStateEmpty_ReturnsError() =>
        _v.TestValidate(Valid() with { State = "" }).ShouldHaveValidationErrorFor(x => x.State);

    [Fact] public void Validate_WhenPincodeEmpty_ReturnsError() =>
        _v.TestValidate(Valid() with { Pincode = "" }).ShouldHaveValidationErrorFor(x => x.Pincode);

    [Fact] public void Validate_WhenGstNumberInvalid_ReturnsError() =>
        _v.TestValidate(Valid() with { GstNumber = "INVALIDGST" }).ShouldHaveValidationErrorFor(x => x.GstNumber);

    [Fact] public void Validate_WhenGstNumberValid_NoErrors() =>
        _v.TestValidate(Valid() with { GstNumber = "29ABCDE1234F1Z5" }).ShouldNotHaveAnyValidationErrors();

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
}

public class UpdateShopCommandValidatorTests
{
    private readonly UpdateShopCommandValidator _v = new();

    private static UpdateShopCommand Valid() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Test Shop", "42 MG Road", "Bengaluru", "Karnataka", "560001", null, null, null);

    [Fact] public void Validate_WhenNameEmpty_ReturnsError() =>
        _v.TestValidate(Valid() with { Name = "" }).ShouldHaveValidationErrorFor(x => x.Name);

    [Fact] public void Validate_WhenGstNumberInvalid_ReturnsError() =>
        _v.TestValidate(Valid() with { GstNumber = "NOTVALID" }).ShouldHaveValidationErrorFor(x => x.GstNumber);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
}

public class AddShopBankAccountCommandValidatorTests
{
    private readonly AddShopBankAccountCommandValidator _v = new();

    private static AddShopBankAccountCommand Valid() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "HDFC", "1234567890", "Savings", "HDFC0001234", "Ravi");

    [Fact] public void Validate_WhenUserIdEmpty_ReturnsError() =>
        _v.TestValidate(Valid() with { UserId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.UserId);

    [Fact] public void Validate_WhenShopIdEmpty_ReturnsError() =>
        _v.TestValidate(Valid() with { ShopId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.ShopId);

    [Fact] public void Validate_WhenAccountTypeInvalid_ReturnsError() =>
        _v.TestValidate(Valid() with { AccountType = "Invalid" }).ShouldHaveValidationErrorFor(x => x.AccountType);

    [Fact] public void Validate_WhenIfscCodeInvalid_ReturnsError() =>
        _v.TestValidate(Valid() with { IfscCode = "INVALIDIFSC" }).ShouldHaveValidationErrorFor(x => x.IfscCode);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
}
