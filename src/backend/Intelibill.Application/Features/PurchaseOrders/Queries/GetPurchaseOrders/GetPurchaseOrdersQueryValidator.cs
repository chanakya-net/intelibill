using FluentValidation;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.PurchaseOrders.Queries.GetPurchaseOrders;

internal sealed class GetPurchaseOrdersQueryValidator : AbstractValidator<GetPurchaseOrdersQuery>
{
    public GetPurchaseOrdersQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThan(0);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .WithErrorCode(Errors.PurchaseOrder.InvalidPageSize.Code);
    }
}
