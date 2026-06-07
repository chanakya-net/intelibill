using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.Commands.CancelPurchaseOrder;
using Intelibill.Application.Features.PurchaseOrders.Commands.CreatePurchaseOrderDraft;
using Intelibill.Application.Features.PurchaseOrders.Commands.DeletePurchaseOrderDraft;
using Intelibill.Application.Features.PurchaseOrders.Commands.PlacePurchaseOrder;
using Intelibill.Application.Features.PurchaseOrders.Commands.ReceivePurchaseOrder;
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
        var list = new PurchaseOrderPagedResultDto([], 0, 1, 20);
        _bus.InvokeAsync<ErrorOr<PurchaseOrderPagedResultDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(ErrorOrFactory.From(list));

        var result = await _controller.GetPurchaseOrders(
            search: "rice",
            status: PurchaseOrderStatus.Draft,
            orderDateFrom: new DateOnly(2026, 6, 1),
            orderDateTo: new DateOnly(2026, 6, 30),
            page: 0,
            pageSize: 999,
            cancellationToken: CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        await _bus.Received(1).InvokeAsync<ErrorOr<PurchaseOrderPagedResultDto>>(
            Arg.Is<GetPurchaseOrdersQuery>(q =>
                q.ActorUserId == userId
                && q.ActiveShopId == shopId
                && q.Search == "rice"
                && q.Status == PurchaseOrderStatus.Draft
                && q.OrderDateFrom == new DateOnly(2026, 6, 1)
                && q.OrderDateTo == new DateOnly(2026, 6, 30)
                && q.Page == 0
                && q.PageSize == 999),
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
            new CreatePurchaseOrderDraftRequest(null, null, null, null, null, "Acme Traders", "SUP-REF-001", []),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(PurchaseOrdersController.GetPurchaseOrderDetail), created.ActionName);
        Assert.Equal(dto, created.Value);
        await _bus.Received(1).InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(
            Arg.Is<CreatePurchaseOrderDraftCommand>(command =>
                command.ActorUserId == userId
                && command.ActiveShopId == shopId
                && command.SupplierName == "Acme Traders"
                && command.SupplierReference == "SUP-REF-001"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatePurchaseOrderDraft_WhenForbidden_ReturnsForbidden()
    {
        SetValidClaims(out _, out _);
        _bus.InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Errors.PurchaseOrder.UserCannotCreatePurchaseOrder);

        var result = await _controller.CreatePurchaseOrderDraft(
            new CreatePurchaseOrderDraftRequest(null, null, null, null, null, null, null, []),
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
            new CreatePurchaseOrderDraftRequest(Guid.NewGuid(), null, null, null, null, null, null, []),
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

    // ---- Place ----

    [Fact]
    public async Task PlacePurchaseOrder_WhenValid_ReturnsOk()
    {
        SetValidClaims(out var userId, out var shopId);
        var poId = Guid.NewGuid();
        var dto = new PurchaseOrderDetailDto(
            poId, "PO-2026-000001", PurchaseOrderStatus.Placed,
            Guid.NewGuid(), null, null, null, null, [], 0m, DateTimeOffset.UtcNow);
        _bus.InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns((ErrorOr<PurchaseOrderDetailDto>)dto);

        var result = await _controller.PlacePurchaseOrder(poId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(dto, okResult.Value);
        await _bus.Received(1).InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(
            Arg.Is<PlacePurchaseOrderCommand>(c =>
                c.ActorUserId == userId && c.ActiveShopId == shopId && c.PurchaseOrderId == poId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlacePurchaseOrder_WhenForbidden_ReturnsForbidden()
    {
        SetValidClaims(out _, out _);
        _bus.InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Errors.PurchaseOrder.UserCannotMutatePurchaseOrder);

        var result = await _controller.PlacePurchaseOrder(Guid.NewGuid(), CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
    }

    [Fact]
    public async Task PlacePurchaseOrder_WhenNotFound_ReturnsNotFound()
    {
        SetValidClaims(out _, out _);
        _bus.InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Errors.PurchaseOrder.NotFound);

        var result = await _controller.PlacePurchaseOrder(Guid.NewGuid(), CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, obj.StatusCode);
    }

    // ---- Delete Draft ----

    [Fact]
    public async Task DeletePurchaseOrderDraft_WhenValid_ReturnsNoContent()
    {
        SetValidClaims(out var userId, out var shopId);
        var poId = Guid.NewGuid();
        _bus.InvokeAsync<ErrorOr<Deleted>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(ErrorOrFactory.From(Result.Deleted));

        var result = await _controller.DeletePurchaseOrderDraft(poId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        await _bus.Received(1).InvokeAsync<ErrorOr<Deleted>>(
            Arg.Is<DeletePurchaseOrderDraftCommand>(c =>
                c.ActorUserId == userId && c.ActiveShopId == shopId && c.PurchaseOrderId == poId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeletePurchaseOrderDraft_WhenCannotDeleteNonDraft_ReturnsBadRequest()
    {
        SetValidClaims(out _, out _);
        _bus.InvokeAsync<ErrorOr<Deleted>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Errors.PurchaseOrder.CannotDeleteNonDraft);

        var result = await _controller.DeletePurchaseOrderDraft(Guid.NewGuid(), CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
    }

    // ---- Cancel ----

    [Fact]
    public async Task CancelPurchaseOrder_WhenValid_ReturnsOk()
    {
        SetValidClaims(out var userId, out var shopId);
        var poId = Guid.NewGuid();
        var dto = new PurchaseOrderDetailDto(
            poId, "PO-2026-000001", PurchaseOrderStatus.Cancelled,
            null, null, null, null, null, [], 0m, DateTimeOffset.UtcNow, "Too expensive");
        _bus.InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns((ErrorOr<PurchaseOrderDetailDto>)dto);

        var result = await _controller.CancelPurchaseOrder(
            poId,
            new CancelPurchaseOrderRequest("Too expensive"),
            CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(dto, okResult.Value);
        await _bus.Received(1).InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(
            Arg.Is<CancelPurchaseOrderCommand>(c =>
                c.ActorUserId == userId && c.ActiveShopId == shopId
                && c.PurchaseOrderId == poId && c.Reason == "Too expensive"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelPurchaseOrder_WhenCannotCancelAfterReceipt_ReturnsUnprocessable()
    {
        SetValidClaims(out _, out _);
        _bus.InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Errors.PurchaseOrder.CannotCancelAfterReceipt);

        var result = await _controller.CancelPurchaseOrder(
            Guid.NewGuid(),
            new CancelPurchaseOrderRequest(null),
            CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
    }

    // ---- Receive ----

    [Fact]
    public async Task ReceivePurchaseOrder_WhenValid_MapsOneLineAndReturnsOk()
    {
        SetValidClaims(out var userId, out var shopId);
        var poId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var dto = new PurchaseOrderDetailDto(
            poId,
            "PO-2026-000001",
            PurchaseOrderStatus.PartiallyReceived,
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            [],
            0m,
            DateTimeOffset.UtcNow);
        _bus.InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns((ErrorOr<PurchaseOrderDetailDto>)dto);

        var result = await _controller.ReceivePurchaseOrder(
            poId,
            new ReceivePurchaseOrderRequest(
                "REF-1",
                "Notes",
                DateTimeOffset.UtcNow,
                [new ReceivePurchaseOrderLineRequest(lineId, "B-1", 1, 10m, 12m, 11m, 5m, false, false, null, null)]),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(dto, ok.Value);
        await _bus.Received(1).InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(
            Arg.Is<ReceivePurchaseOrderCommand>(c =>
                c.ActorUserId == userId
                && c.ActiveShopId == shopId
                && c.PurchaseOrderId == poId
                && c.ReferenceNumber == "REF-1"
                && c.Notes == "Notes"
                && c.Lines.Count == 1
                && c.Lines[0].PurchaseOrderLineId == lineId),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("null-body")]
    [InlineData("null-lines")]
    [InlineData("empty-lines")]
    [InlineData("two-lines")]
    public async Task ReceivePurchaseOrder_WhenLineShapeInvalid_ForwardsEmptyOrInvalidLinesForValidation(string scenario)
    {
        SetValidClaims(out _, out _);
        _bus.InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Errors.PurchaseOrder.ReceiptLineRequired);

        var lineId = Guid.NewGuid();
        ReceivePurchaseOrderRequest? request = scenario switch
        {
            "null-body" => null,
            "null-lines" => new ReceivePurchaseOrderRequest(null, null, null, null!),
            "empty-lines" => new ReceivePurchaseOrderRequest(null, null, null, []),
            _ => new ReceivePurchaseOrderRequest(
                null,
                null,
                null,
                [
                    new ReceivePurchaseOrderLineRequest(lineId, "B-1", 1, 10m, 12m, 11m, 5m, false, false, null, null),
                    new ReceivePurchaseOrderLineRequest(lineId, "B-2", 1, 10m, 12m, 11m, 5m, false, false, null, null),
                ]),
        };

        var result = await _controller.ReceivePurchaseOrder(Guid.NewGuid(), request, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
        await _bus.Received(1).InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(
            Arg.Is<ReceivePurchaseOrderCommand>(c =>
                scenario == "two-lines" ? c.Lines.Count == 2 : c.Lines.Count == 0),
            Arg.Any<CancellationToken>());
    }
}
