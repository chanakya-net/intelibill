using FluentValidation.TestHelper;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Queries.SearchSellables;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Queries.SearchSellables;

public sealed class SearchSellablesQueryValidatorTests
{
    private readonly SearchSellablesQueryValidator _validator = new();

    [Fact]
    public void Validate_WhenSearchTermEmpty_ReturnsError()
    {
        var query = new SearchSellablesQuery(Guid.NewGuid(), Guid.NewGuid(), "   ");

        var result = _validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.SearchTerm)
            .WithErrorCode(Errors.Inventory.SearchTermRequired.Code)
            .WithErrorMessage(Errors.Inventory.SearchTermRequired.Description);
    }

    [Fact]
    public void Validate_WhenValid_NoErrors()
    {
        var query = new SearchSellablesQuery(Guid.NewGuid(), Guid.NewGuid(), "apple");

        var result = _validator.TestValidate(query);

        result.ShouldNotHaveAnyValidationErrors();
    }
}
