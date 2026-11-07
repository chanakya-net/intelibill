using Intelibill.Api.Extensions;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Services.Cancellations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Intelibill.Api.Controllers;

public sealed partial class SalesController
{
    [HttpPost("{saleId:guid}/cancellation-preview")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> PreviewCancellation(Guid saleId, [FromServices] SaleCancellationService service, CancellationToken ct)
    {
        var auth = CheckAuthAndShop(); if (auth is not null) return auth;
        return (await service.PreviewAsync(UserId!.Value, ActiveShopId!.Value, saleId, ct)).ToActionResult(Ok);
    }
    [HttpPost("{saleId:guid}/cancellations")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> CancelSale(Guid saleId, CancelSaleRequest request, [FromServices] SaleCancellationService service, CancellationToken ct)
    {
        var auth = CheckAuthAndShop(); if (auth is not null) return auth;
        var result = await service.CancelAsync(UserId!.Value, ActiveShopId!.Value, saleId, request, ct);
        if (result.IsError && result.FirstError.Code == "SaleCancellation.PostingDisabled") return result.FirstError.ToUnavailableResult();
        return result.ToActionResult(Ok);
    }
}
