using FluentValidation.TestHelper;
using Intelibill.Application.Features.PurchaseOrders.Queries.GetPurchaseOrderDetail;

namespace Intelibill.Application.Unit.Tests.Features.PurchaseOrders.Queries.GetPurchaseOrderDetail;

public class GetPurchaseOrderDetailQueryValidatorTests
{
    private readonly GetPurchaseOrderDetailQueryValidator _validator = new();

    [Fact]
    public void Validate_WhenPurchaseOrderIdIsEmpty_ReturnsError()
    {
        var query = new GetPurchaseOrderDetailQuery(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty);
        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.PurchaseOrderId);
    }
}
