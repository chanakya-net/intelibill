using FluentValidation;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.PurchaseOrders.Queries.GetPurchaseOrders;

internal sealed class GetPurchaseOrdersQueryValidator : AbstractValidator<GetPurchaseOrdersQuery>
{
    public GetPurchaseOrdersQueryValidator()
    {
        RuleFor(x => x)
            .Must(x => x.OrderDateFrom is null || x.OrderDateTo is null || x.OrderDateFrom <= x.OrderDateTo)
            .WithErrorCode(Errors.PurchaseOrder.InvalidOrderDateRange.Code)
            .WithMessage(Errors.PurchaseOrder.InvalidOrderDateRange.Description);
    }
}
