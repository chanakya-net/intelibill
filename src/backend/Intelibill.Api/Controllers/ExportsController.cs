using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Features.Exports.ProfitLoss;
using Intelibill.Application.Features.Exports.ProfitLoss.Queries.ExportProfitLoss;
using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Application.Features.Exports.Sales.Queries.ExportSales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/exports")]
[Authorize]
public sealed class ExportsController : AuthenticatedControllerBase
{
    public ExportsController(IMessageBus bus) : base(bus)
    {
    }

    [HttpGet("sales")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> ExportSales(
        [FromQuery] string format = "xlsx",
        [FromQuery] string level = "summary",
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        CancellationToken cancellationToken = default)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var resolvedEndDate = endDate ?? today;
        var resolvedStartDate = startDate ?? resolvedEndDate.AddDays(-29);

        var result = await Bus.InvokeAsync<ErrorOr<SalesExportResult>>(
            new ExportSalesQuery(
                UserId!.Value,
                ActiveShopId!.Value,
                format,
                level,
                resolvedStartDate,
                resolvedEndDate),
            cancellationToken);

        return result.ToActionResult(export =>
            File(export.Content, export.ContentType, export.FileName));
    }

    [HttpGet("profit-loss")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> ExportProfitLoss(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? type = null,
        [FromQuery] string? search = null,
        [FromQuery] string format = "xlsx",
        CancellationToken cancellationToken = default)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var resolvedTo = to ?? today;
        var resolvedFrom = from ?? resolvedTo.AddDays(-6);

        var result = await Bus.InvokeAsync<ErrorOr<ProfitLossExportResult>>(
            new ExportProfitLossQuery(
                UserId!.Value,
                ActiveShopId!.Value,
                resolvedFrom,
                resolvedTo,
                type,
                search,
                format),
            cancellationToken);

        return result.ToActionResult(export =>
            File(export.Content, export.ContentType, export.FileName));
    }
}
