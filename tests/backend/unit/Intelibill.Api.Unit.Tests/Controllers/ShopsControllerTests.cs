using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Application.Features.Shops.Commands.CreateShop;
using Intelibill.Application.Features.Shops.Commands.SetDefaultShop;
using Intelibill.Application.Features.Shops.Commands.SwitchActiveShop;
using Intelibill.Application.Features.Shops.Queries.GetMyShops;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace Intelibill.Api.Unit.Tests.Controllers;

public class ShopsControllerTests
{
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly ShopsController _controller;

    public ShopsControllerTests()
    {
        _controller = new ShopsController(_bus);
    }

    [Fact]
    public async Task GetMyShops_WhenUserMissing_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.GetMyShops(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetMyShops_WhenSuccessful_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()));

        IReadOnlyList<UserShopDto> shops = [
            new(Guid.NewGuid(), "Main", "Owner", true, DateTimeOffset.UtcNow)
        ];
        _bus.InvokeAsync<ErrorOr<IReadOnlyList<UserShopDto>>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<IReadOnlyList<UserShopDto>>>(shops.ToList()));

        var result = await _controller.GetMyShops(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(shops, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<IReadOnlyList<UserShopDto>>>(
            Arg.Is<GetMyShopsQuery>(q => q.UserId == userId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateShop_WhenSuccessful_ReturnsOkAndDispatchesCommand()
    {
        var userId = Guid.NewGuid();
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()));
        var request = new CreateShopRequest("Main Shop");
        var authResult = CreateAuthResult();
        ArrangeBusResponse<AuthResult>(authResult);

        var result = await _controller.CreateShop(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(authResult, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<AuthResult>>(
            Arg.Is<CreateShopCommand>(c => c.UserId == userId && c.Name == request.Name),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateShop_WhenValidationError_ReturnsBadRequest()
    {
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()));
        ArrangeBusResponse<AuthResult>(Errors.Shop.NameRequired);

        var result = await _controller.CreateShop(new CreateShopRequest("  "), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task SwitchActiveShop_WhenUserMissing_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.SwitchActiveShop(new SwitchActiveShopRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task SwitchActiveShop_WhenSuccessful_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()));
        var authResult = CreateAuthResult(activeShopId: shopId);
        ArrangeBusResponse<AuthResult>(authResult);

        var result = await _controller.SwitchActiveShop(new SwitchActiveShopRequest(shopId), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(authResult, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<AuthResult>>(
            Arg.Is<SwitchActiveShopCommand>(c => c.UserId == userId && c.ShopId == shopId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetDefaultShop_WhenSuccessful_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()));
        var authResult = CreateAuthResult(activeShopId: shopId);
        ArrangeBusResponse<AuthResult>(authResult);

        var result = await _controller.SetDefaultShop(new SetDefaultShopRequest(shopId), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(authResult, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<AuthResult>>(
            Arg.Is<SetDefaultShopCommand>(c => c.UserId == userId && c.ShopId == shopId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetDefaultShop_WhenMembershipMissing_ReturnsForbidden()
    {
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()));
        ArrangeBusResponse<AuthResult>(Errors.Shop.MembershipNotFound);

        var result = await _controller.SetDefaultShop(new SetDefaultShopRequest(Guid.NewGuid()), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
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

    private static AuthResult CreateAuthResult(Guid? activeShopId = null)
    {
        var userId = Guid.NewGuid();
        var shopId = activeShopId ?? Guid.NewGuid();

        return new AuthResult(
            "access-token",
            "refresh-token",
            DateTimeOffset.UtcNow.AddMinutes(15),
            DateTimeOffset.UtcNow.AddDays(7),
            new UserDto(userId, "user@test.com", null, "First", "Last"),
            shopId,
            [new UserShopDto(shopId, "Main", "Owner", true, DateTimeOffset.UtcNow)]);
    }
}