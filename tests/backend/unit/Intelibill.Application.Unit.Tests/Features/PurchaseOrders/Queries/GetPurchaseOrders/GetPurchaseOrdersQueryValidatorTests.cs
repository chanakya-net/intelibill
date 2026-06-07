using FluentValidation.TestHelper;
using Intelibill.Application.Features.PurchaseOrders.Queries.GetPurchaseOrders;

namespace Intelibill.Application.Unit.Tests.Features.PurchaseOrders.Queries.GetPurchaseOrders;

public class GetPurchaseOrdersQueryValidatorTests
{
    private readonly GetPurchaseOrdersQueryValidator _validator = new();

    [Fact]
    public void Validate_WhenPageSizeIsOutOfRange_ReturnsError()
    {
        var query = new GetPurchaseOrdersQuery(Guid.NewGuid(), Guid.NewGuid(), 1, 101);
        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void Validate_WhenPageIsZero_ReturnsError()
    {
        var query = new GetPurchaseOrdersQuery(Guid.NewGuid(), Guid.NewGuid(), 0, 20);
        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Page);
    }
}
