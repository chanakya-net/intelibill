using System.Text.Json;
using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.OfflineSalesSnapshot.DTOs;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.Commands.RecordSaleReturn;
using Intelibill.Application.Features.Sales.Commands.ReserveInvoiceLease;
using Intelibill.Application.Features.Sales.Commands.VoidSaleReturn;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Queries.GetSales;
using Intelibill.Application.Features.Sales.Queries.GetSaleByReturnNumber;
using Intelibill.Application.Features.Sales.Queries.GetSaleDetail;
using Intelibill.Application.Features.Sales.Queries.GetProfitLossReport;
using Intelibill.Application.Features.Sales.Queries.PreviewSale;
using Intelibill.Application.Features.Sales.Queries.PreviewSaleReturn;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/sales")]
[Authorize]
public sealed class SalesController : AuthenticatedControllerBase
{
    private static readonly JsonSerializerOptions NdjsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly IOfflineSalesSnapshotStreamingService _offlineSnapshotStreamingService;

    public SalesController(IMessageBus bus, IOfflineSalesSnapshotStreamingService offlineSnapshotStreamingService) : base(bus)
    {
        _offlineSnapshotStreamingService = offlineSnapshotStreamingService;
    }

    [HttpGet("offline-snapshot/stream")]
    public async Task StreamOfflineSnapshot(CancellationToken cancellationToken)
    {
        if (UserId is null)
        {
            Response.StatusCode = 401;
            return;
        }

        if (ActiveShopId is null)
        {
            Response.StatusCode = 400;
            return;
        }

        var validation = await _offlineSnapshotStreamingService.ValidateAccessAsync(
            UserId.Value,
            ActiveShopId.Value,
            cancellationToken);

        if (validation.IsError)
        {
            Response.StatusCode = validation.FirstError.Type == ErrorType.NotFound ? 401 : 403;
            return;
        }

        var snapshotId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;

        Response.ContentType = "application/x-ndjson; charset=utf-8";
        Response.Headers.CacheControl = "no-store";

        var count = 0;

        try
        {
            await foreach (var record in _offlineSnapshotStreamingService.StreamAsync(
                               ActiveShopId.Value,
                               snapshotId,
                               startedAt,
                               cancellationToken))
            {
                var line = JsonSerializer.Serialize(record, record.GetType(), NdjsonOptions) + "\n";
                await Response.WriteAsync(line, cancellationToken);
                count++;
                if (count == 1 || count % 50 == 0)
                    await Response.Body.FlushAsync(cancellationToken);
            }

            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // client cancelled
        }
        catch (Exception ex)
        {
            // best-effort: emit error record (do not emit complete)
            var error = new OfflineSalesSnapshotErrorRecord(
                new OfflineSalesSnapshotError(snapshotId, "OfflineSnapshot.StreamFailed", ex.Message));

            var line = JsonSerializer.Serialize(error, NdjsonOptions) + "\n";
            await Response.WriteAsync(line, cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    [HttpPost]
    [Authorize(Policy = "OwnerManagerOrStaff")]
    public async Task<IActionResult> RecordSale([FromBody] RecordSaleRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<SaleDto>>(
            new RecordSaleCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                request.IdempotencyKey,
                request.CustomerId,
                request.CustomerName,
                request.CustomerPhone,
                request.PaymentMethod,
                request.PaidAmount,
                request.DueAmount,
                request.Items.Select(i => new RecordSaleItemCommand(
                    i.Barcode,
                    i.BatchNumber,
                    i.ItemName,
                    i.Quantity,
                    i.CostPrice,
                    i.SalesPrice,
                    i.Mrp,
                    i.TaxRatePercent,
                    i.IsPriceIncludingTax,
                    i.InventoryBatchId,
                    i.ItemDiscount is null ? null : new InstantDiscount(i.ItemDiscount.Type, i.ItemDiscount.Value),
                    i.ClientLineKey,
                    i.HsnCode)).ToList(),
                request.SaleDiscount is null
                    ? null
                    : new InstantDiscount(request.SaleDiscount.Type, request.SaleDiscount.Value)),
            cancellationToken);

        return result.ToActionResult(sale => CreatedAtAction(nameof(RecordSale), sale));
    }

    [HttpPost("invoice-leases/reserve")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> ReserveInvoiceLease(
        [FromBody] ReserveInvoiceLeaseRequest request,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<InvoiceLeaseDto>>(
            new ReserveInvoiceLeaseCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                request.DeviceId,
                request.BlockSize),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet]
    public async Task<IActionResult> GetSales(CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<IReadOnlyList<SaleListItemDto>>>(
            new GetSalesQuery(UserId!.Value, ActiveShopId!.Value),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("profit-loss")]
    public async Task<IActionResult> GetProfitLossReport(CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<IReadOnlyList<ProfitLossReportItemDto>>>(
            new GetProfitLossReportQuery(UserId!.Value, ActiveShopId!.Value),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("{saleId:guid}")]
    public async Task<IActionResult> GetSaleDetail(Guid saleId, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<SaleDto>>(
            new GetSaleDetailQuery(UserId!.Value, ActiveShopId!.Value, saleId),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("returns/{returnNumber}")]
    public async Task<IActionResult> GetSaleByReturnNumber(string returnNumber, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var saleIdResult = await Bus.InvokeAsync<ErrorOr<Guid>>(
            new GetSaleByReturnNumberQuery(UserId!.Value, ActiveShopId!.Value, returnNumber),
            cancellationToken);

        if (saleIdResult.IsError)
            return saleIdResult.ToActionResult(_ => NoContent());

        var saleResult = await Bus.InvokeAsync<ErrorOr<SaleDto>>(
            new GetSaleDetailQuery(UserId!.Value, ActiveShopId!.Value, saleIdResult.Value),
            cancellationToken);

        return saleResult.ToActionResult(Ok);
    }

    [HttpPost("preview")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> PreviewSale([FromBody] PreviewSaleRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<SalePreviewDto>>(
            new PreviewSaleQuery(
                UserId!.Value,
                ActiveShopId!.Value,
                new InstantDiscount(request.SaleDiscount.Type, request.SaleDiscount.Value),
                request.Items.Select(i => new PreviewSaleLineQuery(
                    i.InventoryBatchId,
                    i.Barcode,
                    i.BatchNumber,
                    i.ItemName,
                    i.Quantity,
                    i.CostPrice,
                    i.SalesPrice,
                    i.Mrp,
                    i.TaxRatePercent,
                    i.IsPriceIncludingTax,
                    new InstantDiscount(i.ItemDiscount.Type, i.ItemDiscount.Value),
                    i.ClientLineKey,
                    i.HsnCode)).ToList()),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost("{saleId:guid}/returns/preview")]
    public async Task<IActionResult> PreviewSaleReturn(
        Guid saleId,
        [FromBody] PreviewSaleReturnRequest request,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<SaleReturnPreviewDto>>(
            new PreviewSaleReturnQuery(
                UserId!.Value,
                ActiveShopId!.Value,
                saleId,
                request.DueReductionOverrideAmount,
                request.DueOverrideReason,
                request.Items.Select(i => new PreviewSaleReturnItemQuery(
                    i.SaleItemId,
                    i.Quantity,
                    i.Condition,
                    i.ApprovedRefundAmount,
                    i.Notes)).ToList()),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost("{saleId:guid}/returns")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> RecordSaleReturn(
        Guid saleId,
        [FromBody] RecordSaleReturnRequest request,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var commitResult = await Bus.InvokeAsync<ErrorOr<Success>>(
            new RecordSaleReturnCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                saleId,
                request.PayoutMethod,
                request.DueReductionOverrideAmount,
                request.DueOverrideReason,
                request.Notes,
                request.Items.Select(i => new RecordSaleReturnItemCommand(
                    i.SaleItemId,
                    i.Quantity,
                    i.Condition,
                    i.ApprovedRefundAmount,
                    i.Notes)).ToList()),
            cancellationToken);

        if (commitResult.IsError)
            return commitResult.ToActionResult(_ => NoContent());

        var saleResult = await Bus.InvokeAsync<ErrorOr<SaleDto>>(
            new GetSaleDetailQuery(UserId!.Value, ActiveShopId!.Value, saleId),
            cancellationToken);

        return saleResult.ToActionResult(Ok);
    }

    [HttpPost("returns/{saleReturnId:guid}/void")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> VoidSaleReturn(
        Guid saleReturnId,
        [FromBody] VoidSaleReturnRequest request,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<Success>>(
            new VoidSaleReturnCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                saleReturnId,
                request.Reason),
            cancellationToken);

        return result.ToActionResult(_ => NoContent());
    }
}

public sealed record RecordSaleRequest(
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    string IdempotencyKey,
    PaymentMethod PaymentMethod,
    decimal PaidAmount,
    decimal DueAmount,
    IReadOnlyList<RecordSaleItemRequest> Items,
    InstantDiscountRequest? SaleDiscount = null);

public sealed record ReserveInvoiceLeaseRequest(
    string DeviceId,
    int? BlockSize = null);

public sealed record RecordSaleItemRequest(
    string Barcode,
    string BatchNumber,
    string ItemName,
    decimal Quantity,
    decimal CostPrice,
    decimal SalesPrice,
    decimal Mrp,
    decimal TaxRatePercent,
    bool IsPriceIncludingTax,
    Guid InventoryBatchId,
    InstantDiscountRequest? ItemDiscount = null,
    string? ClientLineKey = null,
    string? HsnCode = null);

public sealed record PreviewSaleRequest(
    InstantDiscountRequest SaleDiscount,
    IReadOnlyList<PreviewSaleItemRequest> Items);

public sealed record PreviewSaleItemRequest(
    Guid InventoryBatchId,
    string Barcode,
    string BatchNumber,
    string ItemName,
    decimal Quantity,
    decimal CostPrice,
    decimal SalesPrice,
    decimal Mrp,
    decimal TaxRatePercent,
    bool IsPriceIncludingTax,
    InstantDiscountRequest ItemDiscount,
    string? ClientLineKey = null,
    string? HsnCode = null);

public sealed record InstantDiscountRequest(
    InstantDiscountType Type,
    decimal Value);

public sealed record PreviewSaleReturnRequest(
    decimal? DueReductionOverrideAmount,
    string? DueOverrideReason,
    IReadOnlyList<PreviewSaleReturnItemRequest> Items);

public sealed record PreviewSaleReturnItemRequest(
    Guid SaleItemId,
    decimal Quantity,
    SaleReturnCondition Condition,
    decimal? ApprovedRefundAmount,
    string? Notes);

public sealed record RecordSaleReturnRequest(
    PaymentMethod? PayoutMethod,
    decimal? DueReductionOverrideAmount,
    string? DueOverrideReason,
    string? Notes,
    IReadOnlyList<RecordSaleReturnItemRequest> Items);

public sealed record RecordSaleReturnItemRequest(
    Guid SaleItemId,
    decimal Quantity,
    SaleReturnCondition Condition,
    decimal? ApprovedRefundAmount,
    string? Notes);

public sealed record VoidSaleReturnRequest(string Reason);
