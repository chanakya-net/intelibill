using FluentValidation;

namespace Intelibill.Application.Features.Suppliers.Queries.GetSupplierDirectory;

internal sealed class GetSupplierDirectoryQueryValidator : AbstractValidator<GetSupplierDirectoryQuery>
{
    public GetSupplierDirectoryQueryValidator()
    {
        RuleFor(x => x.Filter.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.Filter.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Filter.Search).MaximumLength(200);
        RuleFor(x => x.Filter.Status).Must(s => s != null && new[] { "all", "active", "inactive" }.Contains(s.Trim().ToLowerInvariant()))
            .WithMessage("Status must be all, active or inactive.");
        RuleFor(x => x.Filter).Must(q => ((long)q.PageNumber - 1) * q.PageSize <= int.MaxValue)
            .WithMessage("The requested page offset is too large.");
    }
}
