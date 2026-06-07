using FluentValidation.TestHelper;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.Queries.GetPurchaseOrders;

namespace Intelibill.Application.Unit.Tests.Features.PurchaseOrders.Queries.GetPurchaseOrders;

public class GetPurchaseOrdersQueryValidatorTests
{
    private readonly GetPurchaseOrdersQueryValidator _validator = new();

    [Fact]
    public void Validate_WhenOrderDateRangeIsInvalid_ReturnsError()
    {
        var query = new GetPurchaseOrdersQuery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            OrderDateFrom: new DateOnly(2026, 6, 2),
            OrderDateTo: new DateOnly(2026, 6, 1));
        var result = _validator.TestValidate(query);
        var failure = result.ShouldHaveValidationErrorFor(x => x).First();
        Assert.Equal(Errors.PurchaseOrder.InvalidOrderDateRange.Code, failure.ErrorCode);
    }

    [Fact]
    public void Validate_WhenPaginationNeedsNormalization_AllowsRequest()
    {
        var query = new GetPurchaseOrdersQuery(Guid.NewGuid(), Guid.NewGuid(), Page: 0, PageSize: 999);
        var result = _validator.TestValidate(query);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
