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
            request.SupplierId,
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
                && c.SupplierId == request.SupplierId
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

    [Fact]
    public async Task AddInventoryBatch_WhenEmptyBatch_ReturnsBadRequest()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var result = await _controller.AddInventoryBatch(new AddInventoryBatchRequest(Array.Empty<AddInventoryBatchRowRequest>()), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task AddInventoryBatch_WhenSomeRowsFail_ReturnsSuccessAndFailureRows()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<AddInventoryResultDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => (ErrorOr<AddInventoryResultDto>)new AddInventoryResultDto(
                    Guid.NewGuid(),
                    "Rice",
                    "111",
                    Guid.NewGuid(),
                    "B-1",
                    10m,
                    10m,
                    null,
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow),
                _ => (ErrorOr<AddInventoryResultDto>)Errors.Inventory.ItemIdentityConflict);

        var request = new AddInventoryBatchRequest(
            new[]
            {
                CreateBatchRow("row-1", "Rice", "111"),
                CreateBatchRow("row-2", "Dal", "222"),
            });

        var result = await _controller.AddInventoryBatch(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<AddInventoryBatchResponse>(ok.Value);

        Assert.Equal(2, payload.RequestedCount);
        Assert.Equal(1, payload.SuccessCount);
        Assert.Equal(1, payload.FailedCount);
        Assert.Single(payload.Succeeded);
        Assert.Single(payload.Failed);
        Assert.Equal("row-2", payload.Failed[0].ClientRowId);
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
            TaxRatePercent: 5m,
            TaxIncluded: false,
            ExpiryDate: null,
            ManufacturingDate: null,
            SupplierId: null,
            ReferenceNumber: "PO-123",
            Notes: "initial",
            PerformedAt: null);

    private static AddInventoryBatchRowRequest CreateBatchRow(string clientRowId, string itemName, string barcode) =>
        new(
            ClientRowId: clientRowId,
            ItemName: itemName,
            Barcode: barcode,
            ItemDescription: null,
            Uom: "kg",
            BatchNumber: "B-1",
            Quantity: 1m,
            CostPrice: 80m,
            Mrp: 120m,
            SalesPrice: 110m,
            TaxRatePercent: 5m,
            TaxIncluded: false,
            ExpiryDate: null,
            ManufacturingDate: null,
            SupplierId: null,
            ReferenceNumber: null,
            Notes: null,
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
