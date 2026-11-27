using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Licensing.Offline;
using System.Text.Json;
using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Shared.Web.Logging;
using Intelibill.Shared.Web.PublicErrors;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.OfflineSalesSnapshot.DTOs;
using Intelibill.Application.Features.Customers.Services;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Queries.GetSales;
using Intelibill.Application.Features.Sales.Queries.SearchSellables;
using Intelibill.Application.Features.Sales.Queries.GetSaleByIdempotencyKey;
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
    private const string SnapshotStreamFailedCode = "OfflineSnapshot.StreamFailed";
    private const string SnapshotStreamFailedMessage = "The offline snapshot could not be completed. Try again later.";
    private readonly IOfflineSalesSnapshotStreamingService _offlineSnapshotStreamingService;
    private readonly OfflineSnapshotPermitStreamer _offlineSnapshotPermitStream;
    private readonly ISafeDiagnosticWriter _diagnostics;
    public SalesController(
        IMessageBus bus,
        IOfflineSalesSnapshotStreamingService offlineSnapshotStreamingService,
        OfflineSnapshotPermitStreamer offlineSnapshotPermitStream,
        ISafeDiagnosticWriter diagnostics) : base(bus)
    {
        _offlineSnapshotStreamingService = offlineSnapshotStreamingService;
        _offlineSnapshotPermitStream = offlineSnapshotPermitStream;
        _diagnostics = diagnostics;
    }
    [HttpGet("offline-snapshot/stream")]
    [LicenceAction(LicenceAction.NewTrading)]
    public async Task StreamOfflineSnapshot(
        [FromQuery] Guid? leaseId = null,
        [FromQuery] Guid? grantId = null,
        [FromQuery] string? deviceId = null,
        [FromQuery] Guid? tillSessionId = null,
        CancellationToken cancellationToken = default)
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

        var isBound = leaseId.HasValue && grantId.HasValue && deviceId is not null;
        if (HasInvalidSnapshotBinding(leaseId, grantId, deviceId, tillSessionId, isBound))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        SessionClaims? session = null;
        if (isBound)
        {
            session = SessionClaims.TryRead(User);
            if (session is null)
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
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

        var (snapshotId, records) = StartSnapshotRecords(
            isBound, session, leaseId, grantId, deviceId, tillSessionId, ActiveShopId.Value, cancellationToken);

        Response.ContentType = "application/x-ndjson; charset=utf-8";
        Response.Headers.CacheControl = "no-store";

        await WriteSnapshotStreamAsync(records, snapshotId, cancellationToken);
    }

    private static bool HasInvalidSnapshotBinding(
        Guid? leaseId, Guid? grantId, string? deviceId, Guid? tillSessionId, bool isBound)
    {
        var hasAnyBinding = leaseId.HasValue || grantId.HasValue || deviceId is not null;
        return hasAnyBinding &&
            (!isBound || leaseId == Guid.Empty || grantId == Guid.Empty || string.IsNullOrWhiteSpace(deviceId) ||
             (tillSessionId.HasValue && tillSessionId.Value == Guid.Empty));
    }

    private (Guid SnapshotId, IAsyncEnumerable<IOfflineSalesSnapshotStreamRecord> Records) StartSnapshotRecords(
        bool isBound,
        SessionClaims? session,
        Guid? leaseId,
        Guid? grantId,
        string? deviceId,
        Guid? tillSessionId,
        Guid shopId,
        CancellationToken cancellationToken)
    {
        if (isBound)
        {
            var bound = _offlineSnapshotPermitStream.Start(
                session!, grantId!.Value, leaseId!.Value, deviceId!, tillSessionId);
            return (bound.SnapshotId, bound.StreamAsync(cancellationToken));
        }

        var snapshotId = Guid.NewGuid();
        var records = _offlineSnapshotStreamingService.StreamAsync(
            shopId,
            snapshotId,
            DateTimeOffset.UtcNow,
            cancellationToken);
        return (snapshotId, records);
    }

    private async Task WriteSnapshotStreamAsync(
        IAsyncEnumerable<IOfflineSalesSnapshotStreamRecord> records,
        Guid snapshotId,
        CancellationToken cancellationToken)
    {
        var buffer = new NdjsonResponseBuffer(Response);
        try
        {
            await foreach (var record in records)
            {
                var line = JsonSerializer.Serialize(record, record.GetType(), NdjsonOptions);
                await buffer.WriteAsync(line, cancellationToken);
            }
            await buffer.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // client cancelled
        }
        catch (Exception exception) when (exception is not SuspensionResponseStoppedException &&
            (buffer.HasFlushed || Response.HasStarted) && !cancellationToken.IsCancellationRequested)
        {
            // Records are already on the wire, so the status can no longer say this failed. The
            // framed error record says it instead, and no complete record follows, so the client
            // keeps its previous snapshot. A failure before the first record is not caught here:
            // the exception middleware answers it as an ordinary failed request. Nor is a caller
            // who disconnected: nobody would read the record.
            var traceId = SafeTraceId.Resolve(HttpContext);
            _diagnostics.Unexpected(
                exception,
                SafeDiagnosticContext.For(
                    HttpContext,
                    traceId,
                    SnapshotStreamFailedCode,
                    StatusCodes.Status500InternalServerError,
                    responseStarted: true));

            var error = new OfflineSalesSnapshotErrorRecord(
                new OfflineSalesSnapshotError(snapshotId, SnapshotStreamFailedCode, SnapshotStreamFailedMessage, traceId));
            var line = JsonSerializer.Serialize(error, NdjsonOptions) + "\n";
            await Response.WriteAsync(line, cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    [HttpPost]
    [Authorize(Policy = "OwnerManagerOrStaff")]
    [LicenceAction(LicenceAction.NewTrading)]
    [SuspensionAtSource(CheckRecovery = true)]
    public async Task<IActionResult> RecordSale([FromBody] RecordSaleRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;
        var session = SessionClaims.TryRead(User);
        if (session is null) return Unauthorized();
        var creditNoteRedemptions = (request.CreditNoteRedemptions ?? []).Select(redemption =>
            new RecordSaleCreditNoteRedemptionCommand(redemption.Code, redemption.Amount)).ToList();
        var creditNoteAppliedAmount = creditNoteRedemptions.Count > 0
            ? creditNoteRedemptions.Sum(redemption => redemption.Amount)
            : request.CreditNoteAppliedAmount;

        var result = await Bus.InvokeAsync<ErrorOr<SaleDto>>(
            new RecordSaleCommand(
                session,
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
                creditNoteAppliedAmount,
                creditNoteRedemptions,
                request.CreditNoteCustomerMismatchConfirmed,
                request.PlaceOfSupplyStateCode,
                request.CreditOverride is null
                    ? null
                    : new CreditOverrideRequest(request.CreditOverride.Reason, request.CreditOverride.ObservedExposure),
                request.Till, request.TenderLines, request.ExpiryApprovals),
            cancellationToken);
        return result.ToActionResult(sale => CreatedAtAction(nameof(RecordSale), sale));
    }

    [HttpGet]
    [LicenceAction(LicenceAction.History)]
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
    [LicenceAction(LicenceAction.History)]
    public async Task<IActionResult> SearchSellables(
        [FromQuery] string? searchTerm,
        [FromQuery] string? barcode,
        CancellationToken cancellationToken,
        [FromQuery] bool includeUnavailable = false)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var isBarcodeLookup = string.IsNullOrWhiteSpace(searchTerm) && !string.IsNullOrWhiteSpace(barcode);
        var effectiveSearchTerm = isBarcodeLookup ? barcode : searchTerm;

        if (string.IsNullOrWhiteSpace(effectiveSearchTerm))
            return new List<Error> { Errors.Inventory.SearchTermRequired }.ToProblemResult();

        var result = await Bus.InvokeAsync<ErrorOr<IReadOnlyList<SellableDto>>>(
            new SearchSellablesQuery(UserId!.Value, ActiveShopId!.Value, effectiveSearchTerm, isBarcodeLookup, includeUnavailable),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("profit-loss")]
    [Authorize(Policy = "OwnerOrManager")]
    [LicenceAction(LicenceAction.History)]
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
    [LicenceAction(LicenceAction.History)]
    public async Task<IActionResult> GetSaleDetail(Guid saleId, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;
        var result = await Bus.InvokeAsync<ErrorOr<SaleDto>>(
            new GetSaleDetailQuery(UserId!.Value, ActiveShopId!.Value, saleId),
            cancellationToken);
        return result.ToActionResult(Ok);
    }

    [HttpGet("by-idempotency-key/{idempotencyKey}")]
    [Authorize(Policy = "OwnerManagerOrStaff")]
    [LicenceAction(LicenceAction.Recovery)]
    public async Task<IActionResult> GetSaleByIdempotencyKey(string idempotencyKey, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;
        var saleIdResult = await Bus.InvokeAsync<ErrorOr<Guid>>(
            new GetSaleByIdempotencyKeyQuery(UserId!.Value, ActiveShopId!.Value, idempotencyKey),
            cancellationToken);
        if (saleIdResult.IsError)
            return saleIdResult.ToActionResult(_ => NoContent());
        var saleResult = await Bus.InvokeAsync<ErrorOr<SaleDto>>(
            new GetSaleDetailQuery(UserId!.Value, ActiveShopId!.Value, saleIdResult.Value),
            cancellationToken);
        return saleResult.ToActionResult(Ok);
    }

    [HttpGet("returns/{returnNumber}")]
    [LicenceAction(LicenceAction.History)]
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
