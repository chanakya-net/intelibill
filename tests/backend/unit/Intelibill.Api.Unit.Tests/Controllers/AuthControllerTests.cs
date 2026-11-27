using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Auth.Commands.Login;
using Intelibill.Application.Features.Auth.Commands.RequestPasswordReset;
using Intelibill.Application.Features.Auth.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace Intelibill.Api.Unit.Tests.Controllers;

public class AuthControllerTests
{
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _controller = new AuthController(_bus);
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
    public async Task RequestPasswordReset_SendsOnlyTheEmailInCommand()
    {
        var request = new RequestPasswordResetRequest("user@test.com");
        ArrangeBusResponse<bool>(true);

        _ = await _controller.RequestPasswordReset(request, CancellationToken.None);

        await _bus.Received(1).InvokeAsync<ErrorOr<bool>>(
            Arg.Is<RequestPasswordResetCommand>(c => c.Email == request.Email),
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
