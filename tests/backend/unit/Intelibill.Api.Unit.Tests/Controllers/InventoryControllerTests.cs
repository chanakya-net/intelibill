using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.AddInventory;
using Intelibill.Application.Features.Inventory.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace Intelibill.Api.Unit.Tests.Controllers;

public class InventoryControllerTests
{
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly InventoryController _controller;

    public InventoryControllerTests()
    {
        _controller = new InventoryController(_bus);
    }

    [Fact]
    public async Task AddInventory_WhenNoUserClaim_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.AddInventory(CreateRequest(), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task AddInventory_WhenNoActiveShopClaim_ReturnsBadRequest()
    {
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()));

        var result = await _controller.AddInventory(CreateRequest(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task AddInventory_WhenValid_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var request = CreateRequest();

        var dto = new AddInventoryResultDto(
            Guid.NewGuid(),
            request.ItemName,
            request.Barcode,
            Guid.NewGuid(),
            request.BatchNumber,
            request.Quantity,
            request.Quantity,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        _bus.InvokeAsync<ErrorOr<AddInventoryResultDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(dto);

        var result = await _controller.AddInventory(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(dto, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<AddInventoryResultDto>>(
            Arg.Is<AddInventoryCommand>(c =>
                c.ActorUserId == userId
                && c.ActiveShopId == shopId
                && c.ItemName == request.ItemName
                && c.Barcode == request.Barcode
                && c.Quantity == request.Quantity),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddInventory_WhenItemIdentityConflicts_ReturnsBadRequest()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<AddInventoryResultDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Errors.Inventory.ItemIdentityConflict);

        var result = await _controller.AddInventory(CreateRequest(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    private static AddInventoryRequest CreateRequest() =>
        new(
            ItemName: "Rice",
            Barcode: "111",
            ItemDescription: "Sona masuri",
            Uom: "kg",
            BatchNumber: "B-1",
            Quantity: 10m,
            CostPrice: 80m,
            Mrp: 120m,
            SalesPrice: 110m,
            MinSalePrice: 100m,
            TaxRatePercent: 5m,
            ExpiryDate: null,
            ManufacturingDate: null,
            ReferenceNumber: "PO-123",
            Notes: "initial",
            PerformedAt: null);

    private void SetUserClaims(params Claim[] claims)
    {
        var identity = claims.Length == 0
            ? new ClaimsIdentity()
            : new ClaimsIdentity(claims, "TestAuthType");

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
            },
        };
    }
}
