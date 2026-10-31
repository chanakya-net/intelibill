using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Api.Options;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.Commands.Login;
using Intelibill.Application.Features.Auth.Commands.LoginWithEmail;
using Intelibill.Application.Features.Auth.Commands.RegisterWithEmail;
using Intelibill.Application.Features.Auth.Commands.RequestPasswordReset;
using Intelibill.Application.Features.Auth.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace Intelibill.Api.Unit.Tests.Controllers;

public class AuthControllerTests
{
    private const string BaseUrl = "https://inventory.test";

    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly AuthController _controller;
    private readonly IEmailOwnershipService _ownership = Substitute.For<IEmailOwnershipService>();

    public AuthControllerTests()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AppOptions { BaseUrl = BaseUrl });
        _controller = new AuthController(_bus, options);
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(_ownership).BuildServiceProvider() } };
    }

    [Fact]
    public async Task RegisterWithEmail_ReturnsAcceptedWithoutCredentials()
    {
        _ownership.RequestAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        var result = await _controller.RegisterWithEmail(new RegisterWithEmailRequest("user@test.com", "Untrusted!1", "First", "Last", "+15551234567"), CancellationToken.None);
        Assert.IsType<AcceptedResult>(result);
        await _ownership.Received(1).RequestAsync("user@test.com", Arg.Any<CancellationToken>());
        Assert.Empty(_bus.ReceivedCalls());
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
    public async Task RegisterWithPhone_WhenConflictError_ReturnsConflict()
    {
        ArrangeBusResponse<AuthResult>(Errors.Auth.PhoneAlreadyInUse);

        var result = await _controller.RegisterWithPhone(
            new RegisterWithPhoneRequest("+15551234567", "First", "Last"),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);

        var details = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(Errors.Auth.PhoneAlreadyInUse.Code, details.Title);
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
    public async Task Login_WhenSuccessful_ReturnsOkAndDispatchesGenericCommand()
    {
        var authResult = CreateAuthResult();
        ArrangeBusResponse<AuthResult>(authResult);
        var request = new LoginRequest("user@test.com", "Pass123!");

        var result = await _controller.Login(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(authResult, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<AuthResult>>(
            Arg.Is<LoginCommand>(c => c.Identifier == request.Identifier && c.Password == request.Password),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestPasswordReset_WhenDeliveryUnavailable_ReturnsServiceUnavailable()
    {
        ArrangeBusResponse<bool>(Error.Failure("Auth.EmailDeliveryUnavailable", "Delivery unavailable"));

        var result = await _controller.RequestPasswordReset(
            new RequestPasswordResetRequest("user@test.com"),
            CancellationToken.None);

        var unavailable = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal("Auth.EmailDeliveryUnavailable", Assert.IsType<ProblemDetails>(unavailable.Value).Title);
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
    public async Task LoginWithEmail_WhenInvalidCredentials_ReturnsUnauthorized()
    {
        ArrangeBusResponse<AuthResult>(Errors.Auth.InvalidCredentials);

        var result = await _controller.LoginWithEmail(
            new LoginWithEmailRequest("user@test.com", "Pass123!"),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);

        var details = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(Errors.Auth.InvalidCredentials.Code, details.Title);
    }

    [Fact]
    public async Task LoginWithEmail_DispatchesGenericCommandForCompatibility()
    {
        var request = new LoginWithEmailRequest("user@test.com", "Pass123!");
        ArrangeBusResponse<AuthResult>(CreateAuthResult());

        _ = await _controller.LoginWithEmail(request, CancellationToken.None);

        await _bus.Received(1).InvokeAsync<ErrorOr<AuthResult>>(
            Arg.Is<LoginCommand>(c => c.Identifier == request.Email && c.Password == request.Password),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_WhenInvalidCredentials_ReturnsUnauthorized()
    {
        ArrangeBusResponse<AuthResult>(Errors.Auth.InvalidCredentials);

        var result = await _controller.Login(
            new LoginRequest("user@test.com", "Pass123!"),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);

        var details = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(Errors.Auth.InvalidCredentials.Code, details.Title);
    }

    [Fact]
    public async Task RefreshToken_WhenUnexpectedError_ReturnsInternalServerError()
    {
        ArrangeBusResponse<AuthResult>(Error.Unexpected("Auth.Unexpected", "Unexpected failure"));

        var result = await _controller.RefreshToken(
            new RefreshTokenRequest("refresh-token"),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
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
    public async Task RevokeToken_WhenUnauthorizedError_ReturnsUnauthorized()
    {
        ArrangeBusResponse<bool>(Errors.Auth.InvalidRefreshToken);

        var result = await _controller.RevokeToken(
            new RevokeTokenRequest("refresh-token"),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);
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

    [Fact]
    public async Task Login_WhenUnexpectedError_ReturnsInternalServerError()
    {
        ArrangeBusResponse<AuthResult>(Error.Unexpected("Auth.Unexpected", "Unexpected failure"));

        var result = await _controller.Login(
            new LoginRequest("user@test.com", "Pass123!"),
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
