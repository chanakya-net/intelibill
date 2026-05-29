using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Application.Features.Sales.Services.Pricing;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Queries.PreviewSale;

public sealed class PreviewSaleQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISaleLineValidator saleLineValidator,
    ISalePricingCalculator pricingCalculator)
{
    public async Task<ErrorOr<SalePreviewDto>> Handle(
        PreviewSaleQuery query,
        CancellationToken cancellationToken)
    {
        if (query.Items is null || query.Items.Count == 0)
            return Errors.Sale.ItemsRequired;

        var user = await userRepository.GetByIdAsync(query.ActorUserId, cancellationToken);
        if (user is null)
            return Error.NotFound("User.NotFound", "User not found.");

        var shop = await shopRepository.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(query.ActorUserId, query.ShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var recordSaleItems = query.Items.Select(i => new RecordSaleItemCommand(
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
            i.ItemDiscount,
            i.ClientLineKey,
            i.HsnCode,
            i.LineType,
            i.ServiceId)).ToList();

        var validationWarnings = new List<string>();
        var validationOrError = await saleLineValidator.ValidateLinesAsync(
            query.ShopId,
            recordSaleItems,
            validationWarnings,
            cancellationToken);

        if (validationOrError.IsError)
            return validationOrError.Errors;

        var validatedLines = validationOrError.Value.Lines;

        var pricingRequest = new SalePricingCalculationRequest(
            query.ShopId,
            DateTimeOffset.UtcNow,
            validatedLines.Select(line =>
            {
                return new SalePricingLineCalculationRequest(
                    line.LineType,
                    line.Batch?.Id ?? Guid.Empty,
                    line.Service?.Id,
                    line.Command.Quantity,
                    line.LineType == SaleLineType.Goods ? line.Batch!.GetProfitCostPrice() : 0m,
                    line.LineType == SaleLineType.Goods ? line.Batch!.SalesPrice : line.Command.SalesPrice,
                    line.LineType == SaleLineType.Goods ? line.Batch!.Mrp : line.Command.Mrp,
                    line.Command.TaxRatePercent,
                    line.Command.IsPriceIncludingTax,
                    line.Command.ItemDiscount ?? new InstantDiscount(InstantDiscountType.None, 0m),
                    line.LineType == SaleLineType.Goods);
            }).ToList(),
            query.SaleDiscount);

        var pricingOrError = await pricingCalculator.CalculateAsync(pricingRequest, cancellationToken);
        if (pricingOrError.IsError)
            return pricingOrError.Errors;

        var pricing = pricingOrError.Value;

        var warnings = BuildWarnings(validatedLines);
        var lineDtos = new List<SalePreviewLineDto>(validatedLines.Count);
        for (var i = 0; i < validatedLines.Count; i++)
        {
            var validated = validatedLines[i];
            var calculated = pricing.Lines[i];

            lineDtos.Add(new SalePreviewLineDto(
                calculated.LineType,
                validated.Item?.Id,
                validated.Service?.Id,
                validated.Item?.Barcode ?? validated.Service?.Code ?? string.Empty,
                validated.Item?.Name ?? validated.Service?.Name ?? validated.Command.ItemName,
                validated.Batch?.Id,
                validated.Batch?.BatchNumber,
                validated.Command.Quantity,
                calculated.CostPrice,
                calculated.SalesPrice,
                validated.LineType == SaleLineType.Goods ? validated.Batch!.Mrp : validated.Command.Mrp,
                calculated.TaxRatePercent,
                calculated.IsPriceIncludingTax,
                calculated.PreTaxAmountBeforeDiscount,
                calculated.ItemDiscountAmount,
                calculated.SaleDiscountAmount,
                calculated.TaxableAmount,
                calculated.TaxAmount,
                calculated.TotalAmount,
                calculated.MaxAllowedItemDiscountFlat,
                calculated.MaxAllowedItemDiscountPercent,
                calculated.ConfiguredBatchRuleId,
                calculated.ConfiguredBatchRulePercentage,
                validated.HasPriceMismatch,
                validated.Command.ClientLineKey));
        }

        return new SalePreviewDto(
            pricing.TotalAmount,
            pricing.TotalTaxableAmount,
            pricing.TotalTaxAmount,
            pricing.TotalDiscountAmount,
            pricing.SaleLevelEligibleSubtotal,
            pricing.ConfiguredSaleRule is null
                ? null
                : new SalePreviewConfiguredSaleRuleDto(
                    pricing.ConfiguredSaleRule.RuleId,
                    pricing.ConfiguredSaleRule.RuleType.ToString(),
                    pricing.ConfiguredSaleRule.Percentage,
                    pricing.ConfiguredSaleRule.ThresholdAmount),
            lineDtos,
            pricing.Infos.Select(i => new SalePreviewInfoDto(i.Code, i.Message)).ToList(),
            warnings);
    }

    private static List<SalePreviewWarningDto> BuildWarnings(IReadOnlyList<ValidatedSaleLine> lines)
    {
        var warnings = new List<SalePreviewWarningDto>();
        foreach (var line in lines)
        {
            if (line.HasPriceMismatch)
            {
                warnings.Add(new SalePreviewWarningDto(
                    "sale_preview.warning.client_price_mismatch",
                    "Client line pricing is stale compared to latest batch pricing.",
                    "warning",
                    line.Batch?.Id,
                    line.Command.ClientLineKey));
            }

            if (!string.Equals(line.Command.ItemName.Trim(), line.Item?.Name ?? line.Service?.Name ?? line.Command.ItemName, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(new SalePreviewWarningDto(
                    "sale_preview.warning.client_item_name_mismatch",
                    "Client line item name differs from current item name.",
                    "warning",
                    line.Batch?.Id,
                    line.Command.ClientLineKey));
            }
        }

        return warnings;
    }
}
