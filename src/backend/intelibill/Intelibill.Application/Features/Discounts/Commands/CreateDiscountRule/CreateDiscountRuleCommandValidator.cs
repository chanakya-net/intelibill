using FluentValidation;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Discounts.Commands.CreateDiscountRule;

public sealed class CreateDiscountRuleCommandValidator : AbstractValidator<CreateDiscountRuleCommand>
{
    public CreateDiscountRuleCommandValidator()
    {
        RuleFor(x => x.ShopId).NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithErrorCode(Errors.Discount.NameRequired.Code)
            .WithMessage(Errors.Discount.NameRequired.Description);

        RuleFor(x => x.Percentage)
            .GreaterThan(0)
            .LessThanOrEqualTo(100)
            .WithErrorCode(Errors.Discount.PercentageOutOfRange.Code)
            .WithMessage(Errors.Discount.PercentageOutOfRange.Description);
    }
}
