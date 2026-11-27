using FluentValidation.TestHelper;
using Intelibill.Application.Features.Users.Commands.ChangeMyPassword;
using Intelibill.Application.Features.Users.Commands.UpdateMyProfile;

namespace Intelibill.Application.Unit.Tests.Features.Users.Validators;

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
