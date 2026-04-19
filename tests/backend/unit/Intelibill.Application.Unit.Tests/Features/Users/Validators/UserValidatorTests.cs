using FluentValidation.TestHelper;
using Intelibill.Application.Features.Users.Commands.AddShopUser;
using Intelibill.Application.Features.Users.Commands.ChangeMyPassword;
using Intelibill.Application.Features.Users.Commands.EditShopUser;
using Intelibill.Application.Features.Users.Commands.UpdateMyProfile;

namespace Intelibill.Application.Unit.Tests.Features.Users.Validators;

public class AddShopUserCommandValidatorTests
{
    private readonly AddShopUserCommandValidator _v = new();

    private static AddShopUserCommand Valid() =>
        new(Guid.NewGuid(), [Guid.NewGuid()], "user@test.com", "First", "Last", "+919876543210", "Pass1234!", "Pass1234!", "Manager");

    [Fact] public void Validate_WhenShopIdsEmpty_ReturnsError() =>
        _v.TestValidate(Valid() with { ShopIds = [] }).ShouldHaveValidationErrorFor(x => x.ShopIds);

    [Fact] public void Validate_WhenEmailInvalid_ReturnsError() =>
        _v.TestValidate(Valid() with { Email = "notanemail" }).ShouldHaveValidationErrorFor(x => x.Email);

    [Fact] public void Validate_WhenPhoneInvalid_ReturnsError() =>
        _v.TestValidate(Valid() with { PhoneNumber = "abc" }).ShouldHaveValidationErrorFor(x => x.PhoneNumber);

    [Fact] public void Validate_WhenPasswordTooShort_ReturnsError() =>
        _v.TestValidate(Valid() with { Password = "short", ConfirmPassword = "short" }).ShouldHaveValidationErrorFor(x => x.Password);

    [Fact] public void Validate_WhenPasswordsMismatch_ReturnsError() =>
        _v.TestValidate(Valid() with { ConfirmPassword = "DifferentPass1!" }).ShouldHaveValidationErrorFor(x => x.ConfirmPassword);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
}

public class ChangeMyPasswordCommandValidatorTests
{
    private readonly ChangeMyPasswordCommandValidator _v = new();

    [Fact] public void Validate_WhenCurrentPasswordEmpty_ReturnsError() =>
        _v.TestValidate(new ChangeMyPasswordCommand(Guid.NewGuid(), "", "NewPass1234!")).ShouldHaveValidationErrorFor(x => x.CurrentPassword);

    [Fact] public void Validate_WhenNewPasswordTooShort_ReturnsError() =>
        _v.TestValidate(new ChangeMyPasswordCommand(Guid.NewGuid(), "OldPass1!", "short")).ShouldHaveValidationErrorFor(x => x.NewPassword);

    [Fact] public void Validate_WhenNewPasswordSameAsCurrent_ReturnsError() =>
        _v.TestValidate(new ChangeMyPasswordCommand(Guid.NewGuid(), "SamePass1!", "SamePass1!")).ShouldHaveValidationErrorFor(x => x.NewPassword);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(new ChangeMyPasswordCommand(Guid.NewGuid(), "OldPass1!", "NewPass1234!")).ShouldNotHaveAnyValidationErrors();
}

public class EditShopUserCommandValidatorTests
{
    private readonly EditShopUserCommandValidator _v = new();

    private static EditShopUserCommand Valid() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "user@test.com", "First", "Last", "+919876543210", "Manager", true);

    [Fact] public void Validate_WhenEmailInvalid_ReturnsError() =>
        _v.TestValidate(Valid() with { Email = "notanemail" }).ShouldHaveValidationErrorFor(x => x.Email);

    [Fact] public void Validate_WhenFirstNameEmpty_ReturnsError() =>
        _v.TestValidate(Valid() with { FirstName = "" }).ShouldHaveValidationErrorFor(x => x.FirstName);

    [Fact] public void Validate_WhenPhoneInvalid_ReturnsError() =>
        _v.TestValidate(Valid() with { PhoneNumber = "abc" }).ShouldHaveValidationErrorFor(x => x.PhoneNumber);

    [Fact] public void Validate_WhenRoleEmpty_ReturnsError() =>
        _v.TestValidate(Valid() with { Role = "" }).ShouldHaveValidationErrorFor(x => x.Role);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
}

public class UpdateMyProfileCommandValidatorTests
{
    private readonly UpdateMyProfileCommandValidator _v = new();

    private static UpdateMyProfileCommand Valid() =>
        new(Guid.NewGuid(), "user@test.com", null, "First", "Last", "en-IN");

    [Fact] public void Validate_WhenEmailInvalid_ReturnsError() =>
        _v.TestValidate(Valid() with { Email = "notanemail" }).ShouldHaveValidationErrorFor(x => x.Email);

    [Fact] public void Validate_WhenFirstNameEmpty_ReturnsError() =>
        _v.TestValidate(Valid() with { FirstName = "" }).ShouldHaveValidationErrorFor(x => x.FirstName);

    [Fact] public void Validate_WhenLanguageUnsupported_ReturnsError() =>
        _v.TestValidate(Valid() with { Language = "fr-FR" }).ShouldHaveValidationErrorFor(x => x.Language);

    [Fact] public void Validate_WhenPhoneInvalid_ReturnsError() =>
        _v.TestValidate(Valid() with { PhoneNumber = "abc" }).ShouldHaveValidationErrorFor(x => x.PhoneNumber);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
}
