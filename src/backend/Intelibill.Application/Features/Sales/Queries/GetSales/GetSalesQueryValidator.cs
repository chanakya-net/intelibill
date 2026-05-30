using FluentValidation;
using Intelibill.Application.Features.Sales.DTOs;

namespace Intelibill.Application.Features.Sales.Queries.GetSales;

internal sealed class GetSalesQueryValidator : AbstractValidator<GetSalesQuery>
{
    public GetSalesQueryValidator()
    {
        RuleFor(x => x)
            .Must(HaveValidDateRange)
            .WithErrorCode("Sales.History.InvalidDateRange")
            .WithMessage("'from' date must be before or equal to 'to' date.");

        RuleFor(x => x.Status)
            .Must(status => SaleHistoryStatus.IsValid(status))
            .WithErrorCode("Sales.History.InvalidStatus")
            .WithMessage($"'status' must be one of: all, {SaleHistoryStatus.Paid}, {SaleHistoryStatus.PartiallyPaid}, {SaleHistoryStatus.Refunded}, {SaleHistoryStatus.Unknown}.");
    }

    private static bool HaveValidDateRange(GetSalesQuery query) =>
        !query.From.HasValue
        || !query.To.HasValue
        || query.From.Value <= query.To.Value;
}
