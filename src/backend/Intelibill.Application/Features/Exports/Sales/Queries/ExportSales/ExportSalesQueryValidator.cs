using FluentValidation;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Exports.Sales.Queries.ExportSales;

public sealed class ExportSalesQueryValidator : AbstractValidator<ExportSalesQuery>
{
    public ExportSalesQueryValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.ShopId)
            .NotEmpty();

        RuleFor(x => x.Format)
            .NotEmpty()
            .Must(f => SalesExportFormat.IsSupported(f))
            .WithMessage("Format must be one of: xlsx, pdf, tallyXml, summary, lineItems");

        RuleFor(x => x.Level)
            .NotEmpty()
            .Must(l => SalesExportLevel.IsSupported(l))
            .WithMessage("Level must be one of: summary, lineItems");

        RuleFor(x => x.StartDate)
            .NotEmpty();

        RuleFor(x => x.EndDate)
            .NotEmpty();

        RuleFor(x => x)
            .Custom((query, context) =>
            {
                if (query.StartDate > query.EndDate)
                {
                    context.AddFailure(nameof(query.StartDate), "Start date must be on or before end date.");
                }

                var daysDifference = (query.EndDate.ToDateTime(TimeOnly.MinValue) - query.StartDate.ToDateTime(TimeOnly.MinValue)).Days;
                if (daysDifference > 366)
                {
                    context.AddFailure(nameof(query.StartDate), "Date range must not exceed 366 days.");
                }
            });
    }
}
