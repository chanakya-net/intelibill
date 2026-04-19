using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Queries.GetSales;
using Intelibill.Application.Features.Sales.Queries.GetSaleDetail;
using Intelibill.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace Intelibill.Api.Unit.Tests.Controllers;

public class SalesControllerTests
{
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly SalesController _controller;

    public SalesControllerTests()
    {
        _controller = new SalesController(_bus);
    }

    private void SetUserClaims(params Claim[] claims)
    {
        var identity = claims.Length == 0
            ? new ClaimsIdentity()
            : new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal },
        };
    }

    private static RecordSaleRequest CreateRequest() =>
        new(
            null,
            "Ravi Kumar",
            "+919876543210",
            PaymentMethod.Cash,
            [new RecordSaleItemRequest("BC-001", "B-01", "Rice", 5m, 80m, 100m, 120m, 18m, false)]);

    private static SaleDto CreateDto() =>
        new(
            Guid.NewGuid(),
            "INV-20260416-ABCD1234",
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            500m,
            45m,
            [],
            []);

    [Fact]
    public async Task RecordSale_WhenNoUserClaim_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.RecordSale(CreateRequest(), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task RecordSale_WhenNoActiveShopClaim_ReturnsBadRequest()
    {
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()));

        var result = await _controller.RecordSale(CreateRequest(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task RecordSale_WhenValid_ReturnsCreated()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var dto = CreateDto();
        _bus.InvokeAsync<ErrorOr<SaleDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(dto);

        var result = await _controller.RecordSale(CreateRequest(), CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);
        Assert.Equal(dto, createdResult.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<SaleDto>>(
            Arg.Is<RecordSaleCommand>(c =>
                c.ActorUserId == userId
                && c.ShopId == shopId
                && c.PaymentMethod == PaymentMethod.Cash
                && c.Items.Count == 1
                && c.Items[0].Barcode == "BC-001"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordSale_WhenItemNotFound_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<SaleDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Errors.Sale.ItemNotFound("BC-999"));

        var result = await _controller.RecordSale(CreateRequest(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public async Task RecordSale_WhenInsufficientStock_ReturnsBadRequest()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<SaleDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Errors.Sale.InsufficientStock("BC-001", "B-01"));

        var result = await _controller.RecordSale(CreateRequest(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetSales_WhenUserMissing_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.GetSales(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetSales_WhenActiveShopMissing_ReturnsProblemResult()
    {
        var userId = Guid.NewGuid();
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()));

        var result = await _controller.GetSales(CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetSales_WhenSuccessful_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        IReadOnlyList<SaleListItemDto> sales = [];
        _bus.InvokeAsync<ErrorOr<IReadOnlyList<SaleListItemDto>>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<IReadOnlyList<SaleListItemDto>>>(sales.ToList()));

        var result = await _controller.GetSales(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(sales, ok.Value);
    }

    [Fact]
    public async Task GetSaleDetail_WhenUserMissing_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.GetSaleDetail(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetSaleDetail_WhenSuccessful_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var sale = new SaleDto(saleId, "INV-001", PaymentMethod.Cash, DateTimeOffset.UtcNow, 500m, 90m, [], []);
        _bus.InvokeAsync<ErrorOr<SaleDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SaleDto>>(sale));

        var result = await _controller.GetSaleDetail(saleId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(sale, ok.Value);
    }
}
