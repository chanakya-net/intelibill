using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Application.Features.Users.Commands.ChangeMyPassword;
using Intelibill.Application.Features.Users.Commands.UpdateMyProfile;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace Intelibill.Api.Unit.Tests.Controllers;

public class UsersControllerTests
{
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly UsersController _controller;

    public UsersControllerTests()
    {
        _controller = new UsersController(_bus);
    }

    [Fact]
    public async Task UpdateMyProfile_WhenUserMissing_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.UpdateMyProfile(
            new UpdateMyProfileRequest("user@test.com", "+15551234567", "First", "Last"),
            CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task UpdateMyProfile_WhenSuccessful_ReturnsOkAndDispatchesCommand()
    {
        var userId = Guid.NewGuid();
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()));

        var request = new UpdateMyProfileRequest("updated@test.com", "+15551234567", "Updated", "User");
        var authResult = CreateAuthResult(userId);
        ArrangeBusResponse<AuthResult>(authResult);

        var result = await _controller.UpdateMyProfile(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(authResult, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<AuthResult>>(
            Arg.Is<UpdateMyProfileCommand>(c =>
                c.UserId == userId
                && c.Email == request.Email
                && c.PhoneNumber == request.PhoneNumber
                && c.FirstName == request.FirstName
                && c.LastName == request.LastName),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateMyProfile_WhenEmailConflict_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()));
        ArrangeBusResponse<AuthResult>(Errors.Auth.EmailAlreadyInUse);

        var result = await _controller.UpdateMyProfile(
            new UpdateMyProfileRequest("duplicate@test.com", null, "First", "Last"),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
    }

    [Fact]
    public async Task ChangeMyPassword_WhenUserMissing_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.ChangeMyPassword(
            new ChangeMyPasswordRequest("OldPass123!", "NewPass123!"),
            CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task ChangeMyPassword_WhenSuccessful_ReturnsOkAndDispatchesCommand()
    {
        var userId = Guid.NewGuid();
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()));
        ArrangeBusResponse<bool>(true);

        var request = new ChangeMyPasswordRequest("OldPass123!", "NewPass123!");
        var result = await _controller.ChangeMyPassword(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Password changed successfully.", GetMessage(ok.Value));

        await _bus.Received(1).InvokeAsync<ErrorOr<bool>>(
            Arg.Is<ChangeMyPasswordCommand>(c =>
                c.UserId == userId
                && c.CurrentPassword == request.CurrentPassword
                && c.NewPassword == request.NewPassword),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangeMyPassword_WhenInvalidCurrentPassword_ReturnsUnauthorized()
    {
        var userId = Guid.NewGuid();
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()));
        ArrangeBusResponse<bool>(Errors.Auth.InvalidCurrentPassword);

        var result = await _controller.ChangeMyPassword(
            new ChangeMyPasswordRequest("WrongOldPass!", "NewPass123!"),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);
    }

    private void ArrangeBusResponse<T>(ErrorOr<T> response)
    {
        _bus.InvokeAsync<ErrorOr<T>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));
    }

    private void SetUserClaims(params Claim[] claims)
    {
        var identity = claims.Length == 0 ? new ClaimsIdentity() : new ClaimsIdentity(claims, "test");
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
    }

    private static AuthResult CreateAuthResult(Guid userId)
    {
        return new AuthResult(
            "access-token",
            "refresh-token",
            DateTimeOffset.UtcNow.AddMinutes(15),
            DateTimeOffset.UtcNow.AddDays(7),
            new UserDto(userId, "updated@test.com", "+15551234567", "Updated", "User"));
    }

    private static string? GetMessage(object? value)
    {
        return value?.GetType().GetProperty("message")?.GetValue(value)?.ToString();
    }
}
