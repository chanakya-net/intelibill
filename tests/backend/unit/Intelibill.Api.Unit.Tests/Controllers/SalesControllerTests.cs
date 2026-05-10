using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.Commands.RecordSaleReturn;
using Intelibill.Application.Features.Sales.Commands.VoidSaleReturn;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Queries.GetSaleByReturnNumber;
using Intelibill.Application.Features.Sales.Queries.GetSales;
using Intelibill.Application.Features.Sales.Queries.GetSaleDetail;
using Intelibill.Application.Features.Sales.Queries.PreviewSale;
using Intelibill.Application.Features.Sales.Queries.PreviewSaleReturn;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
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

    private static RecordSaleRequest CreateRequest(Guid? inventoryBatchId = null) =>
        new(
            null,
            "Ravi Kumar",
            "+919876543210",
            $"sale-{Guid.NewGuid():N}",
            PaymentMethod.Cash,
            500m,
            0m,
            [new RecordSaleItemRequest("BC-001", "B-01", "Rice", 5m, 80m, 100m, 120m, 18m, false, inventoryBatchId ?? Guid.NewGuid())]);

    private static SaleDto CreateDto() =>
        new(
            Guid.NewGuid(),
            "INV-20260416-ABCD1234",
            null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            500m,
            0m,
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
        var batchId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var request = CreateRequest(batchId);
        var dto = CreateDto();
        _bus.InvokeAsync<ErrorOr<SaleDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(dto);

        var result = await _controller.RecordSale(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);
        Assert.Equal(dto, createdResult.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<SaleDto>>(
            Arg.Is<RecordSaleCommand>(c =>
                c.ActorUserId == userId
                && c.ShopId == shopId
                && !string.IsNullOrWhiteSpace(c.IdempotencyKey)
                && c.PaymentMethod == PaymentMethod.Cash
                && c.PaidAmount == 500m
                && c.DueAmount == 0m
                && c.Items.Count == 1
                && c.Items[0].Barcode == "BC-001"
                && c.Items[0].InventoryBatchId == batchId),
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

        var sale = new SaleDto(saleId, "INV-001", null, PaymentMethod.Cash, DateTimeOffset.UtcNow, 500m, 0m, 500m, 90m, [], []);
        _bus.InvokeAsync<ErrorOr<SaleDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SaleDto>>(sale));

        var result = await _controller.GetSaleDetail(saleId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(sale, ok.Value);
    }

    [Fact]
    public async Task GetSaleByReturnNumber_WhenSuccessful_ReturnsOriginalSaleDetail()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var sale = new SaleDto(saleId, "INV-001", null, PaymentMethod.Cash, DateTimeOffset.UtcNow, 500m, 0m, 500m, 90m, [], []);
        _bus.InvokeAsync<ErrorOr<Guid>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Guid>>(saleId));
        _bus.InvokeAsync<ErrorOr<SaleDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SaleDto>>(sale));

        var result = await _controller.GetSaleByReturnNumber("RET-001", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(sale, ok.Value);
        await _bus.Received(1).InvokeAsync<ErrorOr<Guid>>(
            Arg.Is<GetSaleByReturnNumberQuery>(q =>
                q.UserId == userId
                && q.ShopId == shopId
                && q.ReturnNumber == "RET-001"),
            Arg.Any<CancellationToken>());
        await _bus.Received(1).InvokeAsync<ErrorOr<SaleDto>>(
            Arg.Is<GetSaleDetailQuery>(q =>
                q.UserId == userId
                && q.ShopId == shopId
                && q.SaleId == saleId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviewSaleReturn_WhenUserMissing_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.PreviewSaleReturn(
            Guid.NewGuid(),
            new PreviewSaleReturnRequest(null, null, []),
            CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task PreviewSaleReturn_WhenActiveShopMissing_ReturnsBadRequest()
    {
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()));

        var result = await _controller.PreviewSaleReturn(
            Guid.NewGuid(),
            new PreviewSaleReturnRequest(null, null, []),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task PreviewSaleReturn_WhenSuccessful_ReturnsOkAndDispatchesQuery()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var saleItemId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var dto = new SaleReturnPreviewDto(
            saleId,
            HasFinancialAccess: true,
            [],
            new SaleReturnPreviewFinancialDto(100m, 25m, 75m, 90m, 10m, 25m, 0m),
            []);
        _bus.InvokeAsync<ErrorOr<SaleReturnPreviewDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SaleReturnPreviewDto>>(dto));

        var result = await _controller.PreviewSaleReturn(
            saleId,
            new PreviewSaleReturnRequest(
                DueReductionOverrideAmount: 25m,
                DueOverrideReason: "Customer request",
                [new PreviewSaleReturnItemRequest(saleItemId, 1m, SaleReturnCondition.Restockable, 100m, "Sealed")]),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(dto, ok.Value);
        await _bus.Received(1).InvokeAsync<ErrorOr<SaleReturnPreviewDto>>(
            Arg.Is<PreviewSaleReturnQuery>(q =>
                q.ActorUserId == userId
                && q.ShopId == shopId
                && q.SaleId == saleId
                && q.DueReductionOverrideAmount == 25m
                && q.Items.Count == 1
                && q.Items[0].SaleItemId == saleItemId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviewSale_WhenSuccessful_ReturnsOkAndDispatchesQuery()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var dto = new SalePreviewDto(
            TotalAmount: 100m,
            TotalTaxableAmount: 90m,
            TotalTaxAmount: 10m,
            TotalDiscountAmount: 0m,
            SaleLevelEligibleSubtotal: 90m,
            ConfiguredSaleRule: null,
            Lines: [],
            Infos: [],
            Warnings: []);

        _bus.InvokeAsync<ErrorOr<SalePreviewDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SalePreviewDto>>(dto));

        var request = new PreviewSaleRequest(
            new InstantDiscountRequest(InstantDiscountType.None, 0m),
            [
                new PreviewSaleItemRequest(
                    batchId,
                    "BC-001",
                    "B-01",
                    "Rice",
                    1m,
                    80m,
                    100m,
                    120m,
                    18m,
                    false,
                    new InstantDiscountRequest(InstantDiscountType.None, 0m),
                    "line-1"),
            ]);

        var result = await _controller.PreviewSale(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(dto, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<SalePreviewDto>>(
            Arg.Is<PreviewSaleQuery>(q =>
                q.ActorUserId == userId
                && q.ShopId == shopId
                && q.SaleDiscount.Type == InstantDiscountType.None
                && q.Items.Count == 1
                && q.Items[0].InventoryBatchId == batchId
                && q.Items[0].ClientLineKey == "line-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordSaleReturn_WhenUserMissing_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.RecordSaleReturn(
            Guid.NewGuid(),
            new RecordSaleReturnRequest(PaymentMethod.Cash, null, null, null, []),
            CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task RecordSaleReturn_WhenSuccessful_ReturnsRefreshedSaleDetail()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var saleItemId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var sale = new SaleDto(
            saleId,
            "INV-001",
            null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            500m,
            0m,
            500m,
            45m,
            [],
            [])
        {
            Returns = [
                new SaleReturnDto(
                    Guid.NewGuid(),
                    "RET-20260505-ABC123EF",
                    DateTimeOffset.UtcNow,
                    userId,
                    null,
                    100m,
                    0m,
                    100m,
                    90m,
                    10m,
                    [])
            ],
        };

        _bus.InvokeAsync<ErrorOr<Success>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Success>>(Result.Success));
        _bus.InvokeAsync<ErrorOr<SaleDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SaleDto>>(sale));

        var result = await _controller.RecordSaleReturn(
            saleId,
            new RecordSaleReturnRequest(
                PaymentMethod.Cash,
                DueReductionOverrideAmount: null,
                DueOverrideReason: null,
                Notes: "Customer returned sealed item",
                [new RecordSaleReturnItemRequest(saleItemId, 1m, SaleReturnCondition.Restockable, 100m, "Sealed")]),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(sale, ok.Value);
        await _bus.Received(1).InvokeAsync<ErrorOr<Success>>(
            Arg.Is<RecordSaleReturnCommand>(c =>
                c.ActorUserId == userId
                && c.ShopId == shopId
                && c.SaleId == saleId
                && c.PayoutMethod == PaymentMethod.Cash
                && c.Items.Count == 1
                && c.Items[0].SaleItemId == saleItemId),
            Arg.Any<CancellationToken>());
        await _bus.Received(1).InvokeAsync<ErrorOr<SaleDto>>(
            Arg.Is<GetSaleDetailQuery>(q =>
                q.UserId == userId
                && q.ShopId == shopId
                && q.SaleId == saleId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VoidSaleReturn_WhenSuccessful_ReturnsNoContentAndDispatchesCommand()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var saleReturnId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<Success>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Success>>(Result.Success));

        var result = await _controller.VoidSaleReturn(
            saleReturnId,
            new VoidSaleReturnRequest("Duplicate return"),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        await _bus.Received(1).InvokeAsync<ErrorOr<Success>>(
            Arg.Is<VoidSaleReturnCommand>(c =>
                c.ActorUserId == userId
                && c.ShopId == shopId
                && c.SaleReturnId == saleReturnId
                && c.Reason == "Duplicate return"),
            Arg.Any<CancellationToken>());
    }
}
