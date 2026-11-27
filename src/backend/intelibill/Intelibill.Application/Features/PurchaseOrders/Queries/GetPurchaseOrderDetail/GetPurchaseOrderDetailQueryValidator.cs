using FluentValidation;

namespace Intelibill.Application.Features.PurchaseOrders.Queries.GetPurchaseOrderDetail;

internal sealed class GetPurchaseOrderDetailQueryValidator : AbstractValidator<GetPurchaseOrderDetailQuery>
{
    public GetPurchaseOrderDetailQueryValidator()
    {
        RuleFor(x => x.PurchaseOrderId).NotEmpty();
    }
}
