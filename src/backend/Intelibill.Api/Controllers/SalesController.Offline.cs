using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.ReserveInvoiceLease;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Queries.PreviewSale;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

public sealed partial class SalesController : AuthenticatedControllerBase
{
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

    [HttpPost("offline-sync")]
    [Authorize(Policy = "OwnerManagerOrStaff")]
    public async Task<IActionResult> SyncOfflineSales(
        [FromBody] OfflineSalesSyncRequest? request,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        if (request?.Sales is null)
            return new List<Error> { Errors.Sale.OfflineSalesRequired }.ToProblemResult();

        var result = await Bus.InvokeAsync<ErrorOr<OfflineSalesSyncResponseDto>>(
            new SyncOfflineSalesCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                request.DeviceId,
                request.Sales.Select(s => new OfflineSaleSyncCommand(
                    s.ClientSaleId,
                    s.InvoiceNumber,
                    s.SoldAt,
                    s.CustomerId,
                    s.CustomerName,
                    s.CustomerPhone,
                    s.PaymentMethod,
                    s.PaidAmount,
                    s.DueAmount,
                    s.SubtotalBeforeDiscount,
                    s.TotalBeforeDiscount,
                    s.TotalDiscountAmount,
                    s.TotalTaxAmount,
                    s.TotalAmount,
                    s.SaleDiscountOverrideType,
                    s.SaleDiscountOverrideValue,
                    s.ConfiguredSaleRuleId,
                    s.ConfiguredSaleRuleType,
                    s.ConfiguredSaleRulePercentage,
                    s.ConfiguredSaleRuleThresholdAmount,
                    s.Items.Select(i => new OfflineSaleSyncLineCommand(
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
                        i.PreTaxAmountBeforeDiscount,
                        i.ItemDiscountAmount,
                        i.SaleDiscountAmount,
                        i.TaxableAmount,
                        i.TaxAmount,
                        i.TotalAmount,
                        i.ConfiguredBatchRuleId,
                        i.ConfiguredBatchRulePercentage,
                        i.ItemDiscountOverrideType,
                        i.ItemDiscountOverrideValue,
                        i.HsnCode)).ToList()))
                    .ToList()),
            cancellationToken);

        return result.ToActionResult(Ok);
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
                    i.HsnCode,
                    i.LineType,
                    i.ServiceId)).ToList()),
            cancellationToken);

        return result.ToActionResult(Ok);
    }
}
