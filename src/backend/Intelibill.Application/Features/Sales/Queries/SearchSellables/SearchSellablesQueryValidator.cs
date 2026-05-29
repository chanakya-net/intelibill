using FluentValidation;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Sales.Queries.SearchSellables;

internal sealed class SearchSellablesQueryValidator : AbstractValidator<SearchSellablesQuery>
{
    public SearchSellablesQueryValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.ShopId)
            .NotEmpty();

        RuleFor(x => x.SearchTerm)
            .NotEmpty()
            .WithErrorCode(Errors.Inventory.SearchTermRequired.Code)
            .WithMessage(Errors.Inventory.SearchTermRequired.Description);
    }
}
