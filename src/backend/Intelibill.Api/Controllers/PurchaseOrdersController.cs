using ErrorOr;
using Intelibill.Api.Extensions;
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
                request.SupplierId,
                request.OrderDate,
                request.ExpectedDeliveryDate,
                request.SupplierReferenceNumber,
                request.Notes,
                request.SupplierName,
                request.SupplierReference,
                request.Lines.Select(l => new CreatePurchaseOrderLineInput(
                    l.ItemId, l.Description, l.ExpectedQuantity, l.UnitCost)).ToList()),
            cancellationToken);

        return result.ToActionResult(po => CreatedAtAction(
            nameof(GetPurchaseOrderDetail),
            new { purchaseOrderId = po.PurchaseOrderId },
            po));
    }

    [HttpPut("{purchaseOrderId:guid}")]
    public async Task<IActionResult> UpdatePurchaseOrderDraft(
        Guid purchaseOrderId,
        [FromBody] UpdatePurchaseOrderDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(
            new UpdatePurchaseOrderDraftCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                purchaseOrderId,
                request.SupplierId,
                request.OrderDate,
                request.ExpectedDeliveryDate,
                request.SupplierReferenceNumber,
                request.Notes,
                request.Lines.Select(l => new UpdatePurchaseOrderLineInput(
                    l.ItemId, l.Description, l.ExpectedQuantity, l.UnitCost)).ToList()),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost("{purchaseOrderId:guid}/place")]
    public async Task<IActionResult> PlacePurchaseOrder(
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(
            new PlacePurchaseOrderCommand(UserId!.Value, ActiveShopId!.Value, purchaseOrderId),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpDelete("{purchaseOrderId:guid}")]
    public async Task<IActionResult> DeletePurchaseOrderDraft(
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<Deleted>>(
            new DeletePurchaseOrderDraftCommand(UserId!.Value, ActiveShopId!.Value, purchaseOrderId),
            cancellationToken);

        return result.ToActionResult(_ => NoContent());
    }

    [HttpPost("{purchaseOrderId:guid}/cancel")]
    public async Task<IActionResult> CancelPurchaseOrder(
        Guid purchaseOrderId,
        [FromBody] CancelPurchaseOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(
            new CancelPurchaseOrderCommand(UserId!.Value, ActiveShopId!.Value, purchaseOrderId, request.Reason),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost("{purchaseOrderId:guid}/receipts")]
    public async Task<IActionResult> ReceivePurchaseOrder(
        Guid purchaseOrderId,
        [FromBody] ReceivePurchaseOrderRequest? request,
        CancellationToken cancellationToken = default)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var lines = request?.Lines ?? [];

        var result = await Bus.InvokeAsync<ErrorOr<PurchaseOrderDetailDto>>(
            new ReceivePurchaseOrderCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                purchaseOrderId,
                request?.ReferenceNumber,
                request?.Notes,
                request?.ReceivedAt,
                lines.Select(l => new ReceivePurchaseOrderLineInput(
                    l.PurchaseOrderLineId,
                    l.BatchNumber,
                    l.Quantity,
                    l.TotalPurchaseCost,
                    l.Mrp,
                    l.SalesPrice,
                    l.TaxRatePercent,
                    l.TaxIncluded,
                    l.PurchaseTaxIncluded,
                    l.ExpiryDate,
                    l.ManufacturingDate)).ToList()),
            cancellationToken);

        return result.ToActionResult(Ok);
    }
}

public sealed record CreatePurchaseOrderDraftLineRequest(
    Guid ItemId,
    string Description,
    int ExpectedQuantity,
    decimal UnitCost);

public sealed record CreatePurchaseOrderDraftRequest(
    Guid? SupplierId,
    DateOnly? OrderDate,
    DateOnly? ExpectedDeliveryDate,
    string? SupplierReferenceNumber,
    string? Notes,
    string? SupplierName,
    string? SupplierReference,
    IReadOnlyList<CreatePurchaseOrderDraftLineRequest> Lines);

public sealed record UpdatePurchaseOrderDraftLineRequest(
    Guid ItemId,
    string Description,
    int ExpectedQuantity,
    decimal UnitCost);

public sealed record UpdatePurchaseOrderDraftRequest(
    Guid? SupplierId,
    DateOnly? OrderDate,
    DateOnly? ExpectedDeliveryDate,
    string? SupplierReferenceNumber,
    string? Notes,
    IReadOnlyList<UpdatePurchaseOrderDraftLineRequest> Lines);

public sealed record CancelPurchaseOrderRequest(string? Reason);

public sealed record ReceivePurchaseOrderLineRequest(
    Guid PurchaseOrderLineId,
    string BatchNumber,
    int Quantity,
    decimal TotalPurchaseCost,
    decimal Mrp,
    decimal SalesPrice,
    decimal TaxRatePercent,
    bool TaxIncluded,
    bool PurchaseTaxIncluded,
    DateOnly? ExpiryDate,
    DateOnly? ManufacturingDate);

public sealed record ReceivePurchaseOrderRequest(
    string? ReferenceNumber,
    string? Notes,
    DateTimeOffset? ReceivedAt,
    IReadOnlyList<ReceivePurchaseOrderLineRequest> Lines);
