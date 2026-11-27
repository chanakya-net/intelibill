using ErrorOr;
using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Application.Features.Sales.Services.Pricing;
using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Commands.RecordSale;

/// <summary>Validates a sale's lines, authorizes their expiry decisions and prices them.</summary>
public sealed class SaleLinePricer(
    ISaleLineValidator saleLineValidator,
    SaleExpiryGuard expiryGuard,
    ISalePricingCalculator salePricingCalculator)
{
    internal sealed record ValidatedSaleItems(
        IReadOnlyList<ValidatedSaleLine> ValidatedLines,
        IReadOnlyList<SaleExpiryDecision> ExpiryDecisions,
        Dictionary<Guid, Item> ItemById);

    internal async Task<ErrorOr<ValidatedSaleItems>> ValidateLinesAndExpiryAsync(
        RecordSaleCommand command,
        DateOnly businessDate,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var validationResultOrError = await saleLineValidator.ValidateLinesAsync(command.ShopId, command.Items, warnings, cancellationToken);
        if (validationResultOrError.IsError)
            return validationResultOrError.Errors;

        var validatedLines = validationResultOrError.Value.Lines;
        var expiryAuthorization = await expiryGuard.AuthorizeAsync(
            command.ShopId,
            command.ActorUserId,
            businessDate,
            validatedLines,
            command.ExpiryApprovals,
            cancellationToken);
        if (expiryAuthorization.IsError) return expiryAuthorization.Errors;
        var itemById = validatedLines
            .Where(l => l.Item is not null)
            .Select(l => l.Item!)
            .DistinctBy(i => i.Id)
            .ToDictionary(i => i.Id);
        return new ValidatedSaleItems(validatedLines, expiryAuthorization.Value, itemById);
    }

    internal async Task<ErrorOr<SalePricingCalculationResult>> PriceLinesAsync(
        RecordSaleCommand command,
        DateTimeOffset soldAt,
        IReadOnlyList<ValidatedSaleLine> validatedLines,
        InstantDiscount effectiveSaleDiscount,
        CancellationToken cancellationToken)
    {
        var pricingRequest = RecordSaleLineMapping.BuildPricingRequest(
            command,
            soldAt,
            validatedLines,
            effectiveSaleDiscount);
        return await salePricingCalculator.CalculateAsync(pricingRequest, cancellationToken);
    }
}
