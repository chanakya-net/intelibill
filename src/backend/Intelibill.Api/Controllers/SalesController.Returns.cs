using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Features.Sales.Commands.RecordSaleReturn;
using Intelibill.Application.Features.Sales.Commands.VoidSaleReturn;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Queries.GetSaleDetail;
using Intelibill.Application.Features.Sales.Queries.PreviewSaleReturn;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

public sealed partial class SalesController : AuthenticatedControllerBase
{
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
                    i.LineType,
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
                request.PayoutDestination,
                request.DueReductionOverrideAmount,
                request.DueOverrideReason,
                request.Notes,
                request.Items.Select(i => new RecordSaleReturnItemCommand(
                    i.SaleItemId,
                    i.Quantity,
                    i.LineType,
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
