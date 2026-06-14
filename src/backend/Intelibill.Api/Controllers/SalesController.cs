using System.Text.Json;
using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.OfflineSalesSnapshot.DTOs;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Queries.GetSales;
using Intelibill.Application.Features.Sales.Queries.SearchSellables;
using Intelibill.Application.Features.Sales.Queries.GetSaleByReturnNumber;
using Intelibill.Application.Features.Sales.Queries.GetSaleDetail;
using Intelibill.Application.Features.Sales.Queries.GetProfitLossReport;
using Intelibill.Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/sales")]
[Authorize]
public sealed partial class SalesController : AuthenticatedControllerBase
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
                    i.HsnCode,
                    i.LineType,
                    i.ServiceId)).ToList(),
                request.SaleDiscount is null
                    ? null
                    : new InstantDiscount(request.SaleDiscount.Type, request.SaleDiscount.Value),
                request.CreditNoteAppliedAmount,
                request.CreditNoteCode,
                request.CreditNoteCustomerMismatchConfirmed),
            cancellationToken);
        return result.ToActionResult(sale => CreatedAtAction(nameof(RecordSale), sale));
    }

    [HttpGet]
    public async Task<IActionResult> GetSales(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;
        var result = await Bus.InvokeAsync<ErrorOr<SalesHistoryResultDto>>(
            new GetSalesQuery(UserId!.Value, ActiveShopId!.Value, from, to, search, status, page, pageSize),
            cancellationToken);
        return result.ToActionResult(Ok);
    }

    [HttpGet("sellables")]
    public async Task<IActionResult> SearchSellables(
        [FromQuery] string? searchTerm,
        [FromQuery] string? barcode,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var isBarcodeLookup = string.IsNullOrWhiteSpace(searchTerm) && !string.IsNullOrWhiteSpace(barcode);
        var effectiveSearchTerm = isBarcodeLookup ? barcode : searchTerm;

        if (string.IsNullOrWhiteSpace(effectiveSearchTerm))
            return new List<Error> { Errors.Inventory.SearchTermRequired }.ToProblemResult();

        var result = await Bus.InvokeAsync<ErrorOr<IReadOnlyList<SellableDto>>>(
            new SearchSellablesQuery(UserId!.Value, ActiveShopId!.Value, effectiveSearchTerm, isBarcodeLookup),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("profit-loss")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> GetProfitLossReport(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? type = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;
        var result = await Bus.InvokeAsync<ErrorOr<ProfitLossReportResultDto>>(
            new GetProfitLossReportQuery(UserId!.Value, ActiveShopId!.Value, from, to, type, search, page, pageSize),
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
}
