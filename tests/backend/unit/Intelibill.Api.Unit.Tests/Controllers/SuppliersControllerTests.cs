using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Suppliers.Commands.AddSupplier;
using Intelibill.Application.Features.Suppliers.Commands.EditSupplier;
using Intelibill.Application.Features.Suppliers.DTOs;
using Intelibill.Application.Features.Suppliers.Queries.GetSuppliers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace Intelibill.Api.Unit.Tests.Controllers;

public class SuppliersControllerTests
{
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly SuppliersController _controller;

    public SuppliersControllerTests()
    {
        _controller = new SuppliersController(_bus);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    private void SetUserClaims(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var principal = new ClaimsPrincipal(identity);
        _controller.ControllerContext.HttpContext.User = principal;
    }

    [Fact]
    public async Task GetSuppliers_WhenNoUserClaim_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.GetSuppliers(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetSuppliers_WhenNoActiveShopClaim_ReturnsBadRequest()
    {
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()));

        var result = await _controller.GetSuppliers(CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task AddSupplier_WhenValid_ReturnsCreated()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var request = new AddSupplierRequest(
            "Supplier",
            "Contact",
            "+919999999999",
            "Address",
            "City",
            "State",
            "560001",
            true,
            false);

        var dto = new SupplierDto(Guid.NewGuid(), request.Name, request.ContactPersonName, request.ContactPersonPhone, request.Address, request.City, request.State, request.Pin, request.IsActive, request.IsPreferred, 0m);
        _bus.InvokeAsync<ErrorOr<SupplierDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(dto);

        var result = await _controller.AddSupplier(request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(SuppliersController.GetSuppliers), created.ActionName);
        Assert.Equal(dto, created.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<SupplierDto>>(
            Arg.Is<AddSupplierCommand>(c => c.ActorUserId == userId && c.ActiveShopId == shopId && c.Name == request.Name),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EditSupplier_WhenSupplierMissing_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<SupplierDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Errors.Supplier.SupplierNotFound);

        var result = await _controller.EditSupplier(
            Guid.NewGuid(),
            new EditSupplierRequest("Name", null, null, "Address", "City", "State", "560001", true, false),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }
}
