using FluentValidation;

namespace Intelibill.Application.Features.Sales.Queries.GetProfitLossReport;

internal sealed class GetProfitLossReportQueryValidator : AbstractValidator<GetProfitLossReportQuery>
{
    private static readonly string[] AllowedTypes = ["all", "sale", "saleReturn", "inventoryAdjustment"];

    public GetProfitLossReportQueryValidator()
    {
        RuleFor(x => x)
            .Must(HaveValidDateRange)
            .WithErrorCode("Sales.ProfitLoss.InvalidDateRange")
            .WithMessage("'from' date must be before or equal to 'to' date.");

        RuleFor(x => x.Type)
            .Must(BeAllowedType)
            .WithErrorCode("Sales.ProfitLoss.InvalidType")
            .WithMessage("'type' must be one of: all, sale, saleReturn, inventoryAdjustment.");
    }

    private static bool HaveValidDateRange(GetProfitLossReportQuery query) =>
        !query.From.HasValue
        || !query.To.HasValue
        || query.From.Value <= query.To.Value;

    private static bool BeAllowedType(string? type) =>
        type is null || AllowedTypes.Contains(type, StringComparer.OrdinalIgnoreCase);
}
