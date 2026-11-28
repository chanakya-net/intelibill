using Intelibill.Application.Features.Sales.Services;
using Intelibill.Application.Features.Sales.Services.Pricing;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Commands.RecordSale;

/// <summary>Pure mappings from validated sale lines to the pricing request and the domain line inputs.</summary>
internal static class RecordSaleLineMapping
{
    public static SalePricingCalculationRequest BuildPricingRequest(
        RecordSaleCommand command,
        DateTimeOffset soldAt,
        IReadOnlyList<ValidatedSaleLine> validatedLines,
        InstantDiscount effectiveSaleDiscount) =>
        new(
            command.ShopId,
            soldAt,
            validatedLines.Select(line => new SalePricingLineCalculationRequest(
                line.LineType,
                line.Batch?.Id ?? Guid.Empty,
                line.Service?.Id,
                line.Command.Quantity,
                line.LineType == SaleLineType.Goods ? line.Batch!.GetProfitCostPrice() : 0m,
                line.LineType == SaleLineType.Goods ? line.Batch!.SalesPrice : line.Command.SalesPrice,
                line.LineType == SaleLineType.Goods ? line.Batch!.Mrp : line.Command.Mrp,
                line.Command.TaxRatePercent,
                line.LineType == SaleLineType.Goods ? line.Batch!.TaxIncluded : line.Command.IsPriceIncludingTax,
                line.Command.ItemDiscount ?? new InstantDiscount(InstantDiscountType.None, 0m),
                line.LineType == SaleLineType.Goods)).ToList(),
            effectiveSaleDiscount);

    public static List<SaleLineInput> BuildSaleLineInputs(
        RecordSaleCommand command,
        IReadOnlyList<ValidatedSaleLine> validatedLines,
        SalePricingCalculationResult pricing) =>
        validatedLines.Select((line, index) =>
        {
            var calculation = pricing.Lines[index];
            return new SaleLineInput(
                command.ShopId,
                line.LineType,
                line.Item?.Id,
                line.Batch?.Id,
                line.Service?.Id,
                LineName: line.Item?.Name ?? line.Service!.Name,
                LineCode: line.Item?.Barcode ?? line.Service!.Code,
                line.Command.Quantity,
                calculation.CostPrice,
                calculation.SalesPrice,
                line.LineType == SaleLineType.Goods ? line.Batch!.Mrp : line.Command.Mrp,
                calculation.TaxRatePercent,
                calculation.IsPriceIncludingTax,
                line.HasPriceMismatch,
                calculation.PreTaxAmountBeforeDiscount,
                calculation.ItemDiscountAmount,
                calculation.SaleDiscountAmount,
                calculation.TaxableAmount,
                calculation.TaxAmount,
                calculation.TotalAmount,
                calculation.ConfiguredBatchRuleId,
                calculation.ConfiguredBatchRulePercentage,
                (line.Command.ItemDiscount ?? new InstantDiscount(InstantDiscountType.None, 0m)).Type,
                (line.Command.ItemDiscount ?? new InstantDiscount(InstantDiscountType.None, 0m)).Value,
                line.Command.HsnCode);
        }).ToList();
}
