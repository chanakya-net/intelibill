using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Api.Options;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Auth.Commands.RequestPasswordReset;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace Intelibill.Api.Unit.Tests.Controllers;

public class AuthControllerTests
{
    private const string BaseUrl = "https://inventory.test";

    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AppOptions { BaseUrl = BaseUrl });
        _controller = new AuthController(_bus, options);
    }

    [Fact]
    public async Task RegisterWithEmail_WhenSuccessful_ReturnsCreatedAtAction()
    {
        var request = new RegisterWithEmailRequest("user@test.com", "Pass123!", "First", "Last");
        var authResult = CreateAuthResult();
        ArrangeBusResponse<AuthResult>(authResult);

        var result = await _controller.RegisterWithEmail(request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(AuthController.RegisterWithEmail), created.ActionName);
        Assert.Equal(authResult, created.Value);
    }

    [Fact]
    public async Task RegisterWithEmail_WhenConflictError_ReturnsConflict()
    {
        ArrangeBusResponse<AuthResult>(Errors.Auth.EmailAlreadyInUse);

        var result = await _controller.RegisterWithEmail(
            new RegisterWithEmailRequest("user@test.com", "Pass123!", "First", "Last"),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);

        var details = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(Errors.Auth.EmailAlreadyInUse.Code, details.Title);
    }

    [Fact]
    public async Task RegisterWithPhone_WhenSuccessful_ReturnsCreatedAtAction()
    {
        var request = new RegisterWithPhoneRequest("+15551234567", "First", "Last");
        var authResult = CreateAuthResult();
        ArrangeBusResponse<AuthResult>(authResult);

        var result = await _controller.RegisterWithPhone(request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(AuthController.RegisterWithPhone), created.ActionName);
        Assert.Equal(authResult, created.Value);
    }

    [Fact]
    public async Task LoginWithEmail_WhenSuccessful_ReturnsOk()
    {
        var authResult = CreateAuthResult();
        ArrangeBusResponse<AuthResult>(authResult);

        var result = await _controller.LoginWithEmail(
            new LoginWithEmailRequest("user@test.com", "Pass123!"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(authResult, ok.Value);
    }

    [Fact]
    public async Task ExternalLogin_WhenValidationError_ReturnsBadRequest()
    {
        ArrangeBusResponse<AuthResult>(Errors.Auth.UnsupportedProvider);

        var result = await _controller.ExternalLogin(
            new ExternalLoginRequest(ExternalAuthProvider.Facebook, "token", null, null),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);

        var details = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(Errors.Auth.UnsupportedProvider.Code, details.Title);
    }

    [Fact]
    public async Task RequestPasswordReset_WhenCommandReturnsError_StillReturnsOkWithFixedMessage()
    {
        ArrangeBusResponse<bool>(Errors.Auth.UnsupportedProvider);

        var result = await _controller.RequestPasswordReset(
            new RequestPasswordResetRequest("user@test.com"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(
            "If an account with that email exists, a reset link has been sent.",
            GetMessage(ok.Value));
    }

    [Fact]
    public async Task RequestPasswordReset_UsesConfiguredBaseUrlInCommand()
    {
        var request = new RequestPasswordResetRequest("user@test.com");
        ArrangeBusResponse<bool>(true);

        _ = await _controller.RequestPasswordReset(request, CancellationToken.None);

        await _bus.Received(1).InvokeAsync<ErrorOr<bool>>(
            Arg.Is<RequestPasswordResetCommand>(c => c.Email == request.Email && c.AppBaseUrl == BaseUrl),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetPassword_WhenSuccessful_ReturnsOkWithSuccessMessage()
    {
        ArrangeBusResponse<bool>(true);

        var result = await _controller.ResetPassword(
            new ResetPasswordRequest("user@test.com", "token", "NewPass123!"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Password has been reset successfully.", GetMessage(ok.Value));
    }

    [Fact]
    public async Task ResetPassword_WhenUnauthorizedError_ReturnsUnauthorized()
    {
        ArrangeBusResponse<bool>(Errors.Auth.InvalidPasswordResetToken);

        var result = await _controller.ResetPassword(
            new ResetPasswordRequest("user@test.com", "token", "NewPass123!"),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);

        var details = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(Errors.Auth.InvalidPasswordResetToken.Code, details.Title);
    }

    [Fact]
    public async Task RefreshToken_WhenSuccessful_ReturnsOk()
    {
        var authResult = CreateAuthResult();
        ArrangeBusResponse<AuthResult>(authResult);

        var result = await _controller.RefreshToken(
            new RefreshTokenRequest("refresh-token"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(authResult, ok.Value);
    }

    [Fact]
    public async Task RefreshToken_WhenUnauthorizedError_ReturnsUnauthorized()
    {
        ArrangeBusResponse<AuthResult>(Errors.Auth.InvalidRefreshToken);

        var result = await _controller.RefreshToken(
            new RefreshTokenRequest("refresh-token"),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);
    }

    [Fact]
    public async Task RevokeToken_WhenSuccessful_ReturnsNoContent()
    {
        ArrangeBusResponse<bool>(true);

        var result = await _controller.RevokeToken(
            new RevokeTokenRequest("refresh-token"),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task RevokeToken_WhenNotFoundError_ReturnsNotFound()
    {
        ArrangeBusResponse<bool>(Error.NotFound("Auth.RefreshTokenNotFound", "Token not found"));

        var result = await _controller.RevokeToken(
            new RevokeTokenRequest("refresh-token"),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public async Task LoginWithEmail_WhenUnexpectedError_ReturnsInternalServerError()
    {
        ArrangeBusResponse<AuthResult>(Error.Unexpected("Auth.Unexpected", "Unexpected failure"));

        var result = await _controller.LoginWithEmail(
            new LoginWithEmailRequest("user@test.com", "Pass123!"),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }

    private void ArrangeBusResponse<T>(ErrorOr<T> response)
    {
        _bus.InvokeAsync<ErrorOr<T>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));
    }

    private static AuthResult CreateAuthResult()
    {
        return new AuthResult(
            "access-token",
            "refresh-token",
            DateTimeOffset.UtcNow.AddMinutes(15),
            DateTimeOffset.UtcNow.AddDays(7),
            new UserDto(Guid.NewGuid(), "user@test.com", null, "First", "Last"));
    }

    private static string? GetMessage(object? value)
    {
        return value?.GetType().GetProperty("message")?.GetValue(value)?.ToString();
    }
}
