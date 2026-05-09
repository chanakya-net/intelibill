using FluentValidation.TestHelper;
using Intelibill.Application.Features.Auth.Commands.ExternalLogin;
using Intelibill.Application.Features.Auth.Commands.Login;
using Intelibill.Application.Features.Auth.Commands.LoginWithEmail;
using Intelibill.Application.Features.Auth.Commands.RefreshToken;
using Intelibill.Application.Features.Auth.Commands.RegisterWithEmail;
using Intelibill.Application.Features.Auth.Commands.RegisterWithPhone;
using Intelibill.Application.Features.Auth.Commands.RequestPasswordReset;
using Intelibill.Application.Features.Auth.Commands.ResetPassword;
using Intelibill.Application.Features.Auth.Commands.RevokeToken;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Unit.Tests.Features.Auth.Validators;

public class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator _v = new();

    [Fact] public void Validate_WhenIdentifierEmpty_ReturnsError() =>
        _v.TestValidate(new LoginCommand("", "password")).ShouldHaveValidationErrorFor(x => x.Identifier);

    [Fact] public void Validate_WhenIdentifierWhitespace_ReturnsError() =>
        _v.TestValidate(new LoginCommand("   ", "password")).ShouldHaveValidationErrorFor(x => x.Identifier);

    [Fact] public void Validate_WhenPasswordEmpty_ReturnsError() =>
        _v.TestValidate(new LoginCommand("a@b.com", "")).ShouldHaveValidationErrorFor(x => x.Password);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(new LoginCommand("a@b.com", "secret123")).ShouldNotHaveAnyValidationErrors();
}

public class LoginWithEmailCommandValidatorTests
{
    private readonly LoginWithEmailCommandValidator _v = new();

    [Fact] public void Validate_WhenEmailEmpty_ReturnsError() =>
        _v.TestValidate(new LoginWithEmailCommand("", "password")).ShouldHaveValidationErrorFor(x => x.Email);

    [Fact] public void Validate_WhenEmailInvalid_ReturnsError() =>
        _v.TestValidate(new LoginWithEmailCommand("notanemail", "password")).ShouldHaveValidationErrorFor(x => x.Email);

    [Fact] public void Validate_WhenPasswordEmpty_ReturnsError() =>
        _v.TestValidate(new LoginWithEmailCommand("a@b.com", "")).ShouldHaveValidationErrorFor(x => x.Password);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(new LoginWithEmailCommand("a@b.com", "secret123")).ShouldNotHaveAnyValidationErrors();
}

public class RegisterWithEmailCommandValidatorTests
{
    private readonly RegisterWithEmailCommandValidator _v = new();

    [Fact] public void Validate_WhenEmailEmpty_ReturnsError() =>
        _v.TestValidate(new RegisterWithEmailCommand("", "Pass1234!", "First", "Last", "+1234567890")).ShouldHaveValidationErrorFor(x => x.Email);

    [Fact] public void Validate_WhenPasswordTooShort_ReturnsError() =>
        _v.TestValidate(new RegisterWithEmailCommand("a@b.com", "short", "First", "Last", "+1234567890")).ShouldHaveValidationErrorFor(x => x.Password);

    [Fact] public void Validate_WhenPasswordTooLong_ReturnsError() =>
        _v.TestValidate(new RegisterWithEmailCommand("a@b.com", new string('x', 101), "First", "Last", "+1234567890")).ShouldHaveValidationErrorFor(x => x.Password);

    [Fact] public void Validate_WhenFirstNameEmpty_ReturnsError() =>
        _v.TestValidate(new RegisterWithEmailCommand("a@b.com", "Pass1234!", "", "Last", "+1234567890")).ShouldHaveValidationErrorFor(x => x.FirstName);

    [Fact] public void Validate_WhenLastNameEmpty_ReturnsError() =>
        _v.TestValidate(new RegisterWithEmailCommand("a@b.com", "Pass1234!", "First", "", "+1234567890")).ShouldHaveValidationErrorFor(x => x.LastName);

    [Fact] public void Validate_WhenPhoneEmpty_ReturnsError() =>
        _v.TestValidate(new RegisterWithEmailCommand("a@b.com", "Pass1234!", "First", "Last", "")).ShouldHaveValidationErrorFor(x => x.PhoneNumber);

    [Fact] public void Validate_WhenPhoneTooLong_ReturnsError() =>
        _v.TestValidate(new RegisterWithEmailCommand("a@b.com", "Pass1234!", "First", "Last", new string('+', 21))).ShouldHaveValidationErrorFor(x => x.PhoneNumber);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(new RegisterWithEmailCommand("a@b.com", "Pass1234!", "First", "Last", "+919876543210")).ShouldNotHaveAnyValidationErrors();
}

public class RegisterWithPhoneCommandValidatorTests
{
    private readonly RegisterWithPhoneCommandValidator _v = new();

    [Fact] public void Validate_WhenPhoneEmpty_ReturnsError() =>
        _v.TestValidate(new RegisterWithPhoneCommand("", "F", "L")).ShouldHaveValidationErrorFor(x => x.PhoneNumber);

    [Fact] public void Validate_WhenPhoneNotE164_ReturnsError() =>
        _v.TestValidate(new RegisterWithPhoneCommand("9876543210", "F", "L")).ShouldHaveValidationErrorFor(x => x.PhoneNumber);

    [Fact] public void Validate_WhenFirstNameEmpty_ReturnsError() =>
        _v.TestValidate(new RegisterWithPhoneCommand("+919876543210", "", "L")).ShouldHaveValidationErrorFor(x => x.FirstName);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(new RegisterWithPhoneCommand("+919876543210", "First", "Last")).ShouldNotHaveAnyValidationErrors();
}

public class RefreshTokenCommandValidatorTests
{
    private readonly RefreshTokenCommandValidator _v = new();

    [Fact] public void Validate_WhenRefreshTokenEmpty_ReturnsError() =>
        _v.TestValidate(new RefreshTokenCommand("")).ShouldHaveValidationErrorFor(x => x.RefreshToken);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(new RefreshTokenCommand("token-value")).ShouldNotHaveAnyValidationErrors();
}

public class RevokeTokenCommandValidatorTests
{
    private readonly RevokeTokenCommandValidator _v = new();

    [Fact] public void Validate_WhenRefreshTokenEmpty_ReturnsError() =>
        _v.TestValidate(new RevokeTokenCommand("")).ShouldHaveValidationErrorFor(x => x.RefreshToken);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(new RevokeTokenCommand("token-value")).ShouldNotHaveAnyValidationErrors();
}

public class RequestPasswordResetCommandValidatorTests
{
    private readonly RequestPasswordResetCommandValidator _v = new();

    [Fact] public void Validate_WhenEmailEmpty_ReturnsError() =>
        _v.TestValidate(new RequestPasswordResetCommand("", "https://app.com")).ShouldHaveValidationErrorFor(x => x.Email);

    [Fact] public void Validate_WhenEmailInvalid_ReturnsError() =>
        _v.TestValidate(new RequestPasswordResetCommand("notanemail", "https://app.com")).ShouldHaveValidationErrorFor(x => x.Email);

    [Fact] public void Validate_WhenAppBaseUrlEmpty_ReturnsError() =>
        _v.TestValidate(new RequestPasswordResetCommand("a@b.com", "")).ShouldHaveValidationErrorFor(x => x.AppBaseUrl);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(new RequestPasswordResetCommand("a@b.com", "https://app.com")).ShouldNotHaveAnyValidationErrors();
}

public class ResetPasswordCommandValidatorTests
{
    private readonly ResetPasswordCommandValidator _v = new();

    [Fact] public void Validate_WhenEmailEmpty_ReturnsError() =>
        _v.TestValidate(new ResetPasswordCommand("", "tok", "NewPass1!")).ShouldHaveValidationErrorFor(x => x.Email);

    [Fact] public void Validate_WhenTokenEmpty_ReturnsError() =>
        _v.TestValidate(new ResetPasswordCommand("a@b.com", "", "NewPass1!")).ShouldHaveValidationErrorFor(x => x.Token);

    [Fact] public void Validate_WhenNewPasswordTooShort_ReturnsError() =>
        _v.TestValidate(new ResetPasswordCommand("a@b.com", "tok", "short")).ShouldHaveValidationErrorFor(x => x.NewPassword);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(new ResetPasswordCommand("a@b.com", "tok", "NewPass1234!")).ShouldNotHaveAnyValidationErrors();
}

public class ExternalLoginCommandValidatorTests
{
    private readonly ExternalLoginCommandValidator _v = new();

    [Fact] public void Validate_WhenTokenEmpty_ReturnsError() =>
        _v.TestValidate(new ExternalLoginCommand(ExternalAuthProvider.Google, "")).ShouldHaveValidationErrorFor(x => x.Token);

    [Fact] public void Validate_WhenProviderOutOfRange_ReturnsError() =>
        _v.TestValidate(new ExternalLoginCommand((ExternalAuthProvider)999, "token")).ShouldHaveValidationErrorFor(x => x.Provider);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(new ExternalLoginCommand(ExternalAuthProvider.Google, "tok")).ShouldNotHaveAnyValidationErrors();
}
