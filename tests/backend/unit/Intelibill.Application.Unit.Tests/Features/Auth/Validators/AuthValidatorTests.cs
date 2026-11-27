using FluentValidation.TestHelper;
using Intelibill.Application.Features.Auth.Commands.Login;
using Intelibill.Application.Features.Auth.Commands.RefreshToken;
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
        _v.TestValidate(new RequestPasswordResetCommand("")).ShouldHaveValidationErrorFor(x => x.Email);

    [Fact] public void Validate_WhenEmailInvalid_ReturnsError() =>
        _v.TestValidate(new RequestPasswordResetCommand("notanemail")).ShouldHaveValidationErrorFor(x => x.Email);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(new RequestPasswordResetCommand("a@b.com")).ShouldNotHaveAnyValidationErrors();
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
