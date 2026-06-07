using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.Commands.CreatePurchaseOrderDraft;
using Intelibill.Application.Features.PurchaseOrders.Commands.UpdatePurchaseOrderDraft;
using Intelibill.Application.Features.PurchaseOrders.DTOs;
using Intelibill.Application.Features.PurchaseOrders.Queries.GetPurchaseOrderDetail;
using Intelibill.Application.Features.PurchaseOrders.Queries.GetPurchaseOrders;
using Intelibill.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace Intelibill.Api.Unit.Tests.Controllers;

public class PurchaseOrdersControllerTests
{
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly PurchaseOrdersController _controller;

    public PurchaseOrdersControllerTests()
    {
        _controller = new PurchaseOrdersController(_bus);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    private void SetUserClaims(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(identity);
    }

    private void SetValidClaims(out Guid userId, out Guid shopId)
    {
        userId = Guid.NewGuid();
        shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));
    }

    [Fact]
    public async Task GetPurchaseOrders_WhenNoUser_ReturnsUnauthorized()
    {
        SetUserClaims();
        var result = await _controller.GetPurchaseOrders(cancellationToken: CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetPurchaseOrders_WhenNoShop_ReturnsBadRequest()
    {
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()));
        var result = await _controller.GetPurchaseOrders(cancellationToken: CancellationToken.None);
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
    }

    [Fact]
    public async Task GetPurchaseOrders_WhenValid_ReturnsOk()
    {
        SetValidClaims(out var userId, out var shopId);
        var list = new List<PurchaseOrderListItemDto>().AsReadOnly();
        _bus.InvokeAsync<ErrorOr<IReadOnlyList<PurchaseOrderListItemDto>>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(ErrorOrFactory.From<IReadOnlyList<PurchaseOrderListItemDto>>(list));

        var result = await _controller.GetPurchaseOrders(cancellationToken: CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        await _bus.Received(1).InvokeAsync<ErrorOr<IReadOnlyList<PurchaseOrderListItemDto>>>(
            Arg.Is<GetPurchaseOrdersQuery>(q => q.ActorUserId == userId && q.ActiveShopId == shopId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPurchaseOrderDetail_WhenNotFound_ReturnsNotFound()
    {
        SetValidClaims(out _, out _);
        _bus.InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Errors.PurchaseOrder.NotFound);

        var result = await _controller.GetPurchaseOrderDetail(Guid.NewGuid(), CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, obj.StatusCode);
    }

    [Fact]
    public async Task CreatePurchaseOrderDraft_WhenValid_ReturnsCreated()
    {
        SetValidClaims(out var userId, out var shopId);
        var dto = new PurchaseOrderDetailDto(
            Guid.NewGuid(), "PO-2026-000001", PurchaseOrderStatus.Draft,
            null, null, null, null, null, [], 0m, DateTimeOffset.UtcNow);
        _bus.InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns((ErrorOr<PurchaseOrderDetailDto>)dto);

        var result = await _controller.CreatePurchaseOrderDraft(
            new CreatePurchaseOrderDraftRequest(null, null, null, null, null, []),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(PurchaseOrdersController.GetPurchaseOrderDetail), created.ActionName);
        Assert.Equal(dto, created.Value);
    }

    [Fact]
    public async Task CreatePurchaseOrderDraft_WhenForbidden_ReturnsForbidden()
    {
        SetValidClaims(out _, out _);
        _bus.InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Errors.PurchaseOrder.UserCannotCreatePurchaseOrder);

        var result = await _controller.CreatePurchaseOrderDraft(
            new CreatePurchaseOrderDraftRequest(null, null, null, null, null, []),
            CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
    }

    [Fact]
    public async Task CreatePurchaseOrderDraft_WhenSupplierNotFound_ReturnsNotFound()
    {
        SetValidClaims(out _, out _);
        _bus.InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Errors.Supplier.SupplierNotFound);

        var result = await _controller.CreatePurchaseOrderDraft(
            new CreatePurchaseOrderDraftRequest(Guid.NewGuid(), null, null, null, null, []),
            CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, obj.StatusCode);
    }

    [Fact]
    public async Task UpdatePurchaseOrderDraft_WhenValid_ReturnsOk()
    {
        SetValidClaims(out var userId, out var shopId);
        var poId = Guid.NewGuid();
        var dto = new PurchaseOrderDetailDto(
            poId, "PO-2026-000001", PurchaseOrderStatus.Draft,
            null, null, null, "SUP-1", "Updated notes", [], 0m, DateTimeOffset.UtcNow);
        _bus.InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns((ErrorOr<PurchaseOrderDetailDto>)dto);

        var result = await _controller.UpdatePurchaseOrderDraft(
            poId,
            new UpdatePurchaseOrderDraftRequest(null, null, null, "SUP-1", "Updated notes", []),
            CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(dto, okResult.Value);
        await _bus.Received(1).InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(
            Arg.Is<UpdatePurchaseOrderDraftCommand>(q =>
                q.ActorUserId == userId &&
                q.ActiveShopId == shopId &&
                q.PurchaseOrderId == poId &&
                q.SupplierReferenceNumber == "SUP-1" &&
                q.Notes == "Updated notes"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdatePurchaseOrderDraft_WhenNotFound_ReturnsNotFound()
    {
        SetValidClaims(out _, out _);
        var poId = Guid.NewGuid();
        _bus.InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Errors.PurchaseOrder.NotFound);

        var result = await _controller.UpdatePurchaseOrderDraft(
            poId,
            new UpdatePurchaseOrderDraftRequest(null, null, null, null, null, []),
            CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, obj.StatusCode);
    }

    [Fact]
    public async Task UpdatePurchaseOrderDraft_WhenSupplierNotFound_ReturnsNotFound()
    {
        SetValidClaims(out _, out _);
        var poId = Guid.NewGuid();
        _bus.InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Errors.Supplier.SupplierNotFound);

        var result = await _controller.UpdatePurchaseOrderDraft(
            poId,
            new UpdatePurchaseOrderDraftRequest(Guid.NewGuid(), null, null, null, null, []),
            CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, obj.StatusCode);
    }
}
