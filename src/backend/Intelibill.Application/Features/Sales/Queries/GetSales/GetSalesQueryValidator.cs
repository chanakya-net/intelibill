using FluentValidation;

namespace Intelibill.Application.Features.Sales.Queries.GetSales;

internal sealed class GetSalesQueryValidator : AbstractValidator<GetSalesQuery>
{
    public GetSalesQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThan(0);

        RuleFor(x => x.PageSize)
            .GreaterThan(0);

        RuleFor(x => x)
            .Must(HaveValidDateRange)
            .WithErrorCode("Sales.History.InvalidDateRange")
            .WithMessage("'from' date must be before or equal to 'to' date.");
    }

    private static bool HaveValidDateRange(GetSalesQuery query) =>
        !query.From.HasValue
        || !query.To.HasValue
        || query.From.Value <= query.To.Value;
}

