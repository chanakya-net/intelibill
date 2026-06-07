using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Features.PurchaseOrders.Commands.CreatePurchaseOrderDraft;
using Intelibill.Application.Features.PurchaseOrders.DTOs;
using Intelibill.Application.Features.PurchaseOrders.Queries.GetPurchaseOrderDetail;
using Intelibill.Application.Features.PurchaseOrders.Queries.GetPurchaseOrders;
using Intelibill.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/purchase-orders")]
[Authorize]
public sealed class PurchaseOrdersController : AuthenticatedControllerBase
{
    public PurchaseOrdersController(IMessageBus bus) : base(bus)
    {
    }

    [HttpGet]
    public async Task<IActionResult> GetPurchaseOrders(
        [FromQuery] string? search = null,
        [FromQuery] PurchaseOrderStatus? status = null,
        [FromQuery(Name = "order_date_from")] DateOnly? orderDateFrom = null,
        [FromQuery(Name = "order_date_to")] DateOnly? orderDateTo = null,
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<PurchaseOrderPagedResultDto>>(
            new GetPurchaseOrdersQuery(
                UserId!.Value,
                ActiveShopId!.Value,
                search,
                status,
                orderDateFrom,
                orderDateTo,
                page,
                pageSize),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("{purchaseOrderId:guid}")]
    public async Task<IActionResult> GetPurchaseOrderDetail(
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(
            new GetPurchaseOrderDetailQuery(UserId!.Value, ActiveShopId!.Value, purchaseOrderId),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost]
    public async Task<IActionResult> CreatePurchaseOrderDraft(
        [FromBody] CreatePurchaseOrderDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(
            new CreatePurchaseOrderDraftCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                request.Notes,
                request.Lines.Select(l => new CreatePurchaseOrderLineInput(
                    l.Description, l.ExpectedQuantity, l.UnitCost)).ToList()),
            cancellationToken);

        return result.ToActionResult(po => CreatedAtAction(
            nameof(GetPurchaseOrderDetail),
            new { purchaseOrderId = po.PurchaseOrderId },
            po));
    }
}

public sealed record CreatePurchaseOrderDraftLineRequest(
    string Description,
    int ExpectedQuantity,
    decimal UnitCost);

public sealed record CreatePurchaseOrderDraftRequest(
    string? Notes,
    IReadOnlyList<CreatePurchaseOrderDraftLineRequest> Lines);
