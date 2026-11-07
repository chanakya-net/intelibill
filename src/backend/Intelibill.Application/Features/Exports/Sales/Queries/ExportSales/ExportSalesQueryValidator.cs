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

        // An omitted end is the shop's today and an omitted start is 29 days before the end; the
        // handler resolves them and checks the resolved range with the same rules.
        RuleFor(x => x)
            .Custom((query, context) =>
            {
                if (query.StartDate is not { } start || query.EndDate is not { } end)
                    return;

                if (SalesExportRange.Validate(start, end) is { } failure)
                    context.AddFailure(nameof(query.StartDate), failure);
            });
    }
}
